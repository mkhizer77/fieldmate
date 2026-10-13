using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fieldmate.Localization;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Localization;

/// <summary>
/// #24 guard: every English literal passed to Lang.T / Lang.F in the source has a German entry, and each entry keeps
/// the English placeholders, so a new UI string can't silently stay English in German mode.
/// </summary>
public class GermanTextTests
{
    private static readonly Regex Call = new(@"Lang\.[TF]\(", RegexOptions.Compiled);
    private static readonly Regex Literal = new(@"(?<![@$])""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{\d+(?:[:,][^}]*)?\}", RegexOptions.Compiled);

    [Test]
    public void Every_literal_passed_to_Lang_has_a_German_entry()
    {
        var missing = (from file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
                       where !file.Contains("Fieldmate.Tests")
                       let source = File.ReadAllText(file)
                       from english in LiteralsInLangCalls(source)
                       where !GermanText.Table.ContainsKey(english)
                       select $"{Path.GetFileName(file)}: \"{english}\"").Distinct().ToList();

        Assert.That(missing, Is.Empty, "add these to GermanText.Table");
    }

    [Test]
    public void German_entries_keep_the_placeholders_and_are_not_empty()
    {
        foreach (var (english, german) in GermanText.Table.Select(p => (p.Key, p.Value)))
        {
            Assert.That(german, Is.Not.Null.And.Not.Empty, english);
            Assert.That(Placeholder.Matches(german).Select(m => m.Value).OrderBy(x => x),
                Is.EqualTo(Placeholder.Matches(english).Select(m => m.Value).OrderBy(x => x)), english);
        }
    }

    [Test]
    public void The_scan_finds_literals_inside_ternaries()
    {
        var found = LiteralsInLangCalls("x = Lang.T(m == A ? \"grip\" : \"pinch\"); y = Lang.F(\"Step {0} of {1}\", a, b);").ToList();
        Assert.That(found, Is.EquivalentTo(new[] { "grip", "pinch", "Step {0} of {1}" }));
    }

    /// <summary>Plain string literals between "Lang.T(" / "Lang.F(" and the matching close parenthesis.</summary>
    private static System.Collections.Generic.IEnumerable<string> LiteralsInLangCalls(string source)
    {
        foreach (Match call in Call.Matches(source))
        {
            var depth = 1;
            var i = call.Index + call.Length;
            var inString = false;
            for (; i < source.Length && depth > 0; i++)
            {
                var c = source[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                }
                else if (c == '"')
                {
                    inString = true;
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
            }

            var args = source.Substring(call.Index + call.Length, i - call.Index - call.Length - 1);
            // Every literal in the call counts: a ternary of two labels, or a format string.
            foreach (Match literal in Literal.Matches(args))
            {
                yield return Regex.Unescape(literal.Groups[1].Value);
            }
        }
    }
}
