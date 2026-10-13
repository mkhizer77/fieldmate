using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.Json;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;

namespace Fieldmate.AI;

/// <summary>
/// One utterance of the AI eval fixture (design.md §5.4, #17): what the user says, at which step, which tools the model
/// must call (with which arguments), which it must not, and words the answer must contain.
/// </summary>
public sealed class EvalCase
{
    public EvalCase(string id, string utterance, string step, IReadOnlyList<string> tools,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> args, IReadOnlyList<string> forbid, IReadOnlyList<string> keywords)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Case id is required.", nameof(id)) : id;
        Utterance = string.IsNullOrWhiteSpace(utterance) ? throw new ArgumentException($"{id}: utterance is required.", nameof(utterance)) : utterance;
        Step = string.IsNullOrWhiteSpace(step) ? null : step;
        Tools = tools ?? Array.Empty<string>();
        Args = args ?? new Dictionary<string, IReadOnlyDictionary<string, string>>();
        Forbid = forbid ?? Array.Empty<string>();
        Keywords = keywords ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string Utterance { get; }

    /// <summary>The procedure step the user is on (its id), or null before the procedure starts.</summary>
    public string Step { get; }

    /// <summary>Tools that must be called during the turn (any order); "a|b" accepts either.</summary>
    public IReadOnlyList<string> Tools { get; }

    /// <summary>Per tool, arguments that must have the given values (as the transcript shows them).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Args { get; }

    public IReadOnlyList<string> Forbid { get; }

    /// <summary>Words the spoken answer must contain (case-insensitive); "a|b" accepts either.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>Reads <c>{"cases": [...]}</c>.</summary>
    public static IReadOnlyList<EvalCase> ParseFixture(string json)
    {
        var root = JsonReader.Parse(json);
        var cases = new List<EvalCase>();
        foreach (var item in root["cases"].Items)
        {
            var args = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var tool in item["args"].Members)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var arg in tool.Value.Members)
                {
                    values[arg.Key] = arg.Value.Kind == JsonKind.String ? arg.Value.AsString() : arg.Value.ToJson();
                }

                args[tool.Key] = values;
            }

            cases.Add(new EvalCase(item["id"].AsString(), item["utterance"].AsString(), item["step"].AsString(),
                item["tools"].AsStringList(), args, item["forbid"].AsStringList(), item["keywords"].AsStringList()));
        }

        return cases;
    }

    /// <summary>The arguments a model would send for <paramref name="tool"/>, as JSON (mock replies, schema checks).</summary>
    public string ArgumentsJson(string tool)
    {
        if (!Args.TryGetValue(tool, out var values) || values.Count == 0)
        {
            return "{}";
        }

        var members = new List<KeyValuePair<string, JsonValue>>();
        foreach (var pair in values)
        {
            members.Add(new KeyValuePair<string, JsonValue>(pair.Key, JsonReader.TryParse(pair.Value, out var parsed, out _) && parsed.Kind != JsonKind.String
                ? parsed
                : JsonValue.From(pair.Value)));
        }

        return JsonValue.Object(members).ToJson();
    }
}

/// <summary>What one case produced and whether it passed, with the reasons when it didn't.</summary>
public sealed class EvalResult
{
    public EvalResult(EvalCase evalCase, IReadOnlyList<string> toolCalls, string answer, IReadOnlyList<string> failures, double seconds)
    {
        Case = evalCase;
        ToolCalls = toolCalls;
        Answer = answer ?? string.Empty;
        Failures = failures;
        Seconds = seconds;
    }

    public EvalCase Case { get; }

    /// <summary>Each call as the transcript shows it: <c>name(key=value, ...)</c>.</summary>
    public IReadOnlyList<string> ToolCalls { get; }

    public string Answer { get; }
    public IReadOnlyList<string> Failures { get; }
    public double Seconds { get; }
    public bool Passed => Failures.Count == 0;
}

/// <summary>Pure scoring of a turn against its case.</summary>
public static class EvalScorer
{
    public static IReadOnlyList<string> Score(EvalCase evalCase, IReadOnlyList<string> toolCalls, string answer)
    {
        var failures = new List<string>();
        var called = toolCalls.Select(NameOf).ToList();
        foreach (var tool in evalCase.Tools)
        {
            if (!tool.Split('|').Any(t => called.Contains(t.Trim())))
            {
                failures.Add($"expected a {tool.Replace("|", " or ")} call");
            }
        }

        foreach (var tool in evalCase.Forbid)
        {
            if (called.Contains(tool))
            {
                failures.Add($"must not call {tool}");
            }
        }

        foreach (var pair in evalCase.Args)
        {
            foreach (var arg in pair.Value)
            {
                var wanted = $"{arg.Key}={arg.Value}";
                if (!toolCalls.Any(c => NameOf(c) == pair.Key && ArgumentsOf(c).Contains(wanted)))
                {
                    failures.Add($"expected {pair.Key}({wanted})");
                }
            }
        }

        var text = answer ?? string.Empty;
        foreach (var keyword in evalCase.Keywords)
        {
            var options = keyword.Split('|');
            if (!options.Any(o => text.IndexOf(o.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
            {
                failures.Add($"answer lacks \"{keyword}\"");
            }
        }

        return failures;
    }

    private static string NameOf(string call)
    {
        var paren = call.IndexOf('(');
        return paren < 0 ? call.Trim() : call.Substring(0, paren).Trim();
    }

    private static IReadOnlyList<string> ArgumentsOf(string call)
    {
        var open = call.IndexOf('(');
        var close = call.IndexOf(')', open + 1);
        if (open < 0 || close < 0)
        {
            return Array.Empty<string>();
        }

        return call.Substring(open + 1, close - open - 1).Split(',').Select(s => s.Trim()).ToList();
    }
}

/// <summary>
/// Runs eval cases through a real <see cref="AssistantSession"/> (system prompt, retrieval, tools, scene fake) with any
/// chat model: a scripted mock in CI (contract tests), the real model from the editor menu (Fieldmate/Run AI Eval).
/// Each case gets a fresh machine at its step, so cases don't affect each other.
/// </summary>
public sealed class EvalHarness
{
    private readonly MachineManual manual;
    private readonly Func<EvalCase, IChatModel> chatFor;

    public EvalHarness(MachineManual manual, Func<EvalCase, IChatModel> chatFor)
    {
        this.manual = manual ?? throw new ArgumentNullException(nameof(manual));
        this.chatFor = chatFor ?? throw new ArgumentNullException(nameof(chatFor));
    }

    public async Task<EvalResult> RunAsync(EvalCase evalCase, CancellationToken cancellationToken)
    {
        var runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        DemoProcedures.ApplyInitialStates(runner);
        var telemetry = new TelemetryModel(FaultModel.CreateDefault());
        telemetry.Faults.Inject(FaultModel.Overpressure);
        var clock = 0d;
        if (evalCase.Step != null)
        {
            ProcedureReplay.AdvanceTo(runner, evalCase.Step, ref clock);
        }

        var scene = new EvalScene(() => clock);
        var registry = FieldmateTools.CreateRegistry(includeVision: false);
        new AssistantTools(manual, runner, telemetry, scene).AttachTo(registry);
        var retriever = new ManualRetriever(manual);
        var session = new AssistantSession(chatFor(evalCase), null, null, registry, new ConversationState(),
            language => AssistantPrompt.System(manual.MachineName, language, vision: false),
            text => AssistantPrompt.Context(runner, telemetry, null,
                retriever.Retrieve(new RetrievalQuery(null, runner.State == RunnerState.Running ? runner.CurrentStep.Id : null, text))),
            () => clock);

        var calls = new List<string>();
        session.TranscriptAdded += entry =>
        {
            if (entry.Kind == TranscriptKind.Tool)
            {
                var arrow = entry.Text.IndexOf(" → ", StringComparison.Ordinal);
                calls.Add(arrow < 0 ? entry.Text : entry.Text.Substring(0, arrow));
            }
        };

        var started = DateTime.UtcNow;
        var turn = await session.RunTextTurnAsync(evalCase.Utterance, null, cancellationToken);
        var answer = turn.AssistantText ?? string.Empty;
        var failures = new List<string>(EvalScorer.Score(evalCase, calls, answer));
        if (!turn.Success)
        {
            failures.Insert(0, $"turn failed ({turn.ErrorType})");
        }

        return new EvalResult(evalCase, calls, answer, failures, (DateTime.UtcNow - started).TotalSeconds);
    }

    private sealed class EvalScene : IAssistantScene
    {
        private readonly Func<double> clock;

        public EvalScene(Func<double> clock) => this.clock = clock;

        public double Now => clock();
        public bool TryHighlightPart(string partId) => true;
        public void ShowManualSection(ManualSection section) { }
        public void ShowStep(ProcedureDefinition procedure, int stepNumber) { }
        public void AddNote(string text) { }
        public string IdentifyView() => null;
        public void ShowPartLabels(bool visible) { }
    }
}

/// <summary>The markdown report the editor run writes to docs/eval/.</summary>
public static class EvalReport
{
    public static string Markdown(IReadOnlyList<EvalResult> results, string model, DateTime when)
    {
        var passed = results.Count(r => r.Passed);
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("# AI eval: ").Append(passed).Append('/').Append(results.Count).Append(" passed\n\n");
        sb.Append("Model `").Append(model).Append("`, ").Append(when.ToString("yyyy-MM-dd HH:mm", c)).Append(" UTC. ");
        sb.Append("Median turn ").Append(Median(results.Select(r => r.Seconds)).ToString("0.0", c)).Append(" s.\n\n");
        sb.Append("| | Case | Step | Tools called | Notes |\n|---|---|---|---|---|\n");
        foreach (var r in results)
        {
            sb.Append("| ").Append(r.Passed ? "✅" : "❌").Append(" | `").Append(r.Case.Id).Append("` | ")
                .Append(r.Case.Step ?? "—").Append(" | ").Append(Cell(string.Join("; ", r.ToolCalls))).Append(" | ")
                .Append(Cell(r.Passed ? string.Empty : string.Join("; ", r.Failures))).Append(" |\n");
        }

        sb.Append("\n## Answers\n\n");
        foreach (var r in results)
        {
            sb.Append("- **").Append(r.Case.Id).Append("** — “").Append(r.Case.Utterance).Append("”: ").Append(Cell(r.Answer)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Cell(string text) => (text ?? string.Empty).Replace("|", "\\|").Replace("\n", " ");

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
        {
            return 0d;
        }

        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2d;
    }
}
