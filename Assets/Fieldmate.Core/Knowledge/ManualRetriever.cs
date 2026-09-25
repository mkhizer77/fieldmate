using System;
using System.Collections.Generic;
using System.Text;

namespace Fieldmate.Knowledge;

/// <summary>What the assistant knows about the current moment: gaze target, active step and the user's words.</summary>
public readonly struct RetrievalQuery
{
    public RetrievalQuery(string gazePartId = null, string stepId = null, string text = null)
    {
        GazePartId = gazePartId;
        StepId = stepId;
        Text = text;
    }

    public string GazePartId { get; }
    public string StepId { get; }
    public string Text { get; }
}

/// <summary>The sections chosen for one request, within the size budget.</summary>
public sealed class ManualSlice
{
    public ManualSlice(IReadOnlyList<ManualSection> sections, int characters, bool truncated)
    {
        Sections = sections;
        Characters = characters;
        Truncated = truncated;
    }

    public IReadOnlyList<ManualSection> Sections { get; }

    /// <summary>Characters of section text included.</summary>
    public int Characters { get; }

    /// <summary>Relevant sections were left out to stay within the budget.</summary>
    public bool Truncated { get; }

    /// <summary>Prompt block with one "[section-id] Title" header per section, so answers can cite ids.</summary>
    public string ToPromptText()
    {
        var sb = new StringBuilder();
        foreach (var section in Sections)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append('[').Append(section.Id).Append("] ").Append(section.Title).Append('\n').Append(section.Text).Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>
/// Picks the manual sections relevant to a request (design.md §5.4): sections about the gazed part and the current
/// step, plus keyword overlap with the user's words. No embeddings. Safety sections tied to the gazed part or step
/// are boosted so hazards are always in context. Results are deterministic (ties break by section id).
/// </summary>
public sealed class ManualRetriever
{
    public const int GazeWeight = 3;
    public const int StepWeight = 4;
    public const int SafetyBoost = 2;
    public const int KeywordWeight = 2;
    public const int TextWeight = 1;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "your", "with", "this", "that", "what", "why", "how", "when",
        "where", "which", "who", "can", "does", "did", "was", "were", "has", "have", "had", "its", "into", "from", "then",
        "than", "there", "here", "about", "should", "would", "could", "will", "just", "now", "please", "tell", "show",
    };

    private readonly MachineManual manual;
    private readonly SectionTerms[] terms;

    public ManualRetriever(MachineManual manual, int maxSections = 4, int maxCharacters = 1800)
    {
        this.manual = manual ?? throw new ArgumentNullException(nameof(manual));
        MaxSections = maxSections > 0 ? maxSections : throw new ArgumentOutOfRangeException(nameof(maxSections));
        MaxCharacters = maxCharacters > 0 ? maxCharacters : throw new ArgumentOutOfRangeException(nameof(maxCharacters));

        terms = new SectionTerms[manual.Sections.Count];
        for (var i = 0; i < terms.Length; i++)
        {
            var section = manual.Sections[i];
            var keywordTerms = new HashSet<string>(StringComparer.Ordinal);
            AddTokens(section.Title, keywordTerms);
            foreach (var keyword in section.Keywords)
            {
                AddTokens(keyword, keywordTerms);
            }

            var textTerms = new HashSet<string>(StringComparer.Ordinal);
            AddTokens(section.Text, textTerms);
            terms[i] = new SectionTerms(keywordTerms, textTerms);
        }
    }

    public int MaxSections { get; }
    public int MaxCharacters { get; }

    public ManualSlice Retrieve(in RetrievalQuery query)
    {
        var queryTerms = new HashSet<string>(StringComparer.Ordinal);
        AddTokens(query.Text, queryTerms);

        var candidates = new List<(int score, ManualSection section)>();
        for (var i = 0; i < manual.Sections.Count; i++)
        {
            var score = Score(manual.Sections[i], terms[i], query, queryTerms);
            if (score > 0)
            {
                candidates.Add((score, manual.Sections[i]));
            }
        }

        candidates.Sort((a, b) =>
        {
            var byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.section.Id, b.section.Id);
        });

        var chosen = new List<ManualSection>();
        var characters = 0;
        var truncated = false;
        foreach (var (_, section) in candidates)
        {
            if (chosen.Count == MaxSections || characters + section.Text.Length > MaxCharacters)
            {
                truncated = true;
                continue;
            }

            chosen.Add(section);
            characters += section.Text.Length;
        }

        return new ManualSlice(chosen, characters, truncated);
    }

    /// <summary>Relevance of one section; 0 means unrelated.</summary>
    public int Score(ManualSection section, in RetrievalQuery query)
    {
        var index = IndexOf(section);
        if (index < 0)
        {
            throw new ArgumentException($"Section '{section?.Id}' is not part of this manual.", nameof(section));
        }

        var queryTerms = new HashSet<string>(StringComparer.Ordinal);
        AddTokens(query.Text, queryTerms);
        return Score(section, terms[index], query, queryTerms);
    }

    /// <summary>Lower-case word tokens of at least three letters, stop words removed, simple plural stripped.</summary>
    public static IReadOnlyCollection<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        AddTokens(text, tokens);
        return tokens;
    }

    private static int Score(ManualSection section, SectionTerms sectionTerms, in RetrievalQuery query, HashSet<string> queryTerms)
    {
        var score = 0;
        var anchored = false;
        if (query.GazePartId != null && Contains(section.PartIds, query.GazePartId))
        {
            score += GazeWeight;
            anchored = true;
        }

        if (query.StepId != null && Contains(section.StepIds, query.StepId))
        {
            score += StepWeight;
            anchored = true;
        }

        if (anchored && section.Kind == SectionKind.Safety)
        {
            score += SafetyBoost;
        }

        foreach (var term in queryTerms)
        {
            if (sectionTerms.Keywords.Contains(term))
            {
                score += KeywordWeight;
            }
            else if (sectionTerms.Text.Contains(term))
            {
                score += TextWeight;
            }
        }

        return score;
    }

    private int IndexOf(ManualSection section)
    {
        for (var i = 0; i < manual.Sections.Count; i++)
        {
            if (ReferenceEquals(manual.Sections[i], section))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddTokens(string text, HashSet<string> into)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWord && start < 0)
            {
                start = i;
            }
            else if (!isWord && start >= 0)
            {
                AddToken(text.Substring(start, i - start).ToLowerInvariant(), into);
                start = -1;
            }
        }
    }

    private static void AddToken(string token, HashSet<string> into)
    {
        if (token.Length < 3 || StopWords.Contains(token))
        {
            return;
        }

        if (token.Length > 4 && token[token.Length - 1] == 's' && token[token.Length - 2] != 's')
        {
            token = token.Substring(0, token.Length - 1);
        }

        into.Add(token);
    }

    private readonly struct SectionTerms
    {
        public SectionTerms(HashSet<string> keywords, HashSet<string> text)
        {
            Keywords = keywords;
            Text = text;
        }

        public HashSet<string> Keywords { get; }
        public HashSet<string> Text { get; }
    }
}
