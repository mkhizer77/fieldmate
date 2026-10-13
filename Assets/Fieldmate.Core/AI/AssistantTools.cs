using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;

namespace Fieldmate.AI;

/// <summary>What the tool executors need from the running scene. Implemented by Unity components; faked in tests.</summary>
public interface IAssistantScene
{
    /// <summary>Highlights a placed part. False when the part exists in the manual but not in the scene.</summary>
    bool TryHighlightPart(string partId);

    /// <summary>Shows a manual section on the user's panel.</summary>
    void ShowManualSection(ManualSection section);

    /// <summary>Shows a procedure step (1-based) on the step panel without changing progress.</summary>
    void ShowStep(ProcedureDefinition procedure, int stepNumber);

    void AddNote(string text);

    /// <summary>Shows or hides the name labels on every part (#73); the step's highlight is unaffected.</summary>
    void ShowPartLabels(bool visible);

    /// <summary>
    /// Captures one camera frame and identifies what the user looks at (design.md §5.5). Null when the scene has no vision
    /// at all; otherwise an answer, or a reason it couldn't (camera denied, request failed).
    /// </summary>
    Task<ViewAnswer?> IdentifyViewAsync(CancellationToken cancellationToken);

    /// <summary>Scene clock in seconds, used to start procedures.</summary>
    double Now { get; }
}

/// <summary>What "What's this?" produced: an identification to relay, or why there is none.</summary>
public readonly struct ViewAnswer
{
    public ViewAnswer(bool identified, string text)
    {
        Identified = identified;
        Text = text ?? string.Empty;
    }

    public static ViewAnswer Of(string text) => new(true, text);
    public static ViewAnswer Unavailable(string reason) => new(false, reason);

    public bool Identified { get; }
    public string Text { get; }
}

/// <summary>Runs an awaitable tool, e.g. one that waits for the camera and a model.</summary>
public sealed class AsyncDelegateToolExecutor : IToolExecutor
{
    private readonly Func<ToolCall, ToolArguments, CancellationToken, Task<ToolResult>> run;

    public AsyncDelegateToolExecutor(Func<ToolCall, ToolArguments, CancellationToken, Task<ToolResult>> run) =>
        this.run = run ?? throw new ArgumentNullException(nameof(run));

    public Task<ToolResult> ExecuteAsync(ToolCall call, ToolArguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return run(call, arguments, cancellationToken);
    }
}

/// <summary>Runs a tool from a delegate. Keeps the v1 executors free of MonoBehaviours so they are unit-testable.</summary>
public sealed class DelegateToolExecutor : IToolExecutor
{
    private readonly Func<ToolCall, ToolArguments, ToolResult> run;

    public DelegateToolExecutor(Func<ToolCall, ToolArguments, ToolResult> run) =>
        this.run = run ?? throw new ArgumentNullException(nameof(run));

    public Task<ToolResult> ExecuteAsync(ToolCall call, ToolArguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(run(call, arguments));
    }
}

/// <summary>
/// The v1 tool executors (design.md §5.4). Each returns a short result the model can read back to the user; unknown ids
/// come back as errors that list what exists, so the model can correct itself.
/// </summary>
public sealed class AssistantTools
{
    private readonly MachineManual manual;
    private readonly PartCatalog catalog;
    private readonly ProcedureRunner runner;
    private readonly TelemetryModel telemetry;
    private readonly IAssistantScene scene;
    private readonly Dictionary<string, ProcedureDefinition> procedures = new(StringComparer.Ordinal);

    public AssistantTools(MachineManual manual, ProcedureRunner runner, TelemetryModel telemetry, IAssistantScene scene)
    {
        this.manual = manual ?? throw new ArgumentNullException(nameof(manual));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        catalog = PartCatalog.FromManual(manual);
        procedures[runner.Definition.Id] = runner.Definition;
    }

    /// <summary>Raised by set_language with "en" or "de".</summary>
    public event Action<string> LanguageChanged;

    /// <summary>Attaches every v1 executor to the registry.</summary>
    public void AttachTo(ToolRegistry registry)
    {
        registry.SetExecutor(FieldmateTools.HighlightPart, new DelegateToolExecutor(HighlightPart));
        registry.SetExecutor(FieldmateTools.StartProcedure, new DelegateToolExecutor(StartProcedure));
        registry.SetExecutor(FieldmateTools.GoToStep, new DelegateToolExecutor(GoToStep));
        registry.SetExecutor(FieldmateTools.ShowManual, new DelegateToolExecutor(ShowManual));
        registry.SetExecutor(FieldmateTools.ReadTelemetry, new DelegateToolExecutor(ReadTelemetry));
        registry.SetExecutor(FieldmateTools.ReportReading, new DelegateToolExecutor(ReportReading));
        registry.SetExecutor(FieldmateTools.LogNote, new DelegateToolExecutor(LogNote));
        registry.SetExecutor(FieldmateTools.SetLanguage, new DelegateToolExecutor(SetLanguage));
        registry.SetExecutor(FieldmateTools.ShowLabels, new DelegateToolExecutor(ShowLabels));
        if (registry.TryGetDefinition(FieldmateTools.IdentifyView, out _))
        {
            registry.SetExecutor(FieldmateTools.IdentifyView, new AsyncDelegateToolExecutor(IdentifyViewAsync));
        }
    }

    public ToolResult HighlightPart(ToolCall call, ToolArguments args)
    {
        var id = args.GetString("part_id");
        if (!catalog.TryGet(id, out var part))
        {
            return ToolResult.Failure(call, $"Unknown part '{id}'. Known parts: {JoinIds(catalog.All, p => p.Id)}.");
        }

        return scene.TryHighlightPart(id)
            ? ToolResult.Success(call, $"Highlighted the {part.Name} for the user: it pulses cyan and a cyan marker with its name floats above it. {part.Description}")
            : ToolResult.Failure(call, $"The {part.Name} is not placed in the scene yet.");
    }

    public ToolResult ShowLabels(ToolCall call, ToolArguments args)
    {
        var visible = args.GetBool("visible", true);
        scene.ShowPartLabels(visible);
        return ToolResult.Success(call, visible
            ? $"Name labels are now shown on all {catalog.All.Count} parts. The current step's part keeps its cyan highlight."
            : "Part labels are hidden again; only the current step's part is labelled.");
    }

    public ToolResult StartProcedure(ToolCall call, ToolArguments args)
    {
        var id = args.GetString("procedure_id");
        if (!procedures.TryGetValue(id, out var procedure))
        {
            return ToolResult.Failure(call, $"Unknown procedure '{id}'. Available: {string.Join(", ", procedures.Keys)}.");
        }

        if (runner.State == RunnerState.Running)
        {
            return ToolResult.Failure(call,
                $"'{procedure.Title}' is already running at step {runner.CurrentStepIndex + 1}: {runner.CurrentStep.Title}.");
        }

        runner.Restart(scene.Now);
        scene.ShowStep(procedure, 1);
        return ToolResult.Success(call, $"Started '{procedure.Title}'. Step 1 of {procedure.Steps.Count}: {procedure.Steps[0].Title}.");
    }

    public ToolResult GoToStep(ToolCall call, ToolArguments args)
    {
        var procedure = runner.Definition;
        var number = args.GetInt("index");
        if (runner.State == RunnerState.NotStarted)
        {
            return ToolResult.Failure(call, "No procedure is running. Start one first.");
        }

        if (number < 1 || number > procedure.Steps.Count)
        {
            return ToolResult.Failure(call, $"'{procedure.Title}' has steps 1 to {procedure.Steps.Count}.");
        }

        scene.ShowStep(procedure, number);
        var current = runner.State == RunnerState.Running ? $"The current step is still {runner.CurrentStepIndex + 1}." : "The procedure is complete.";
        return ToolResult.Success(call, $"Showing step {number} of {procedure.Steps.Count}: {procedure.Steps[number - 1].Title}. {current}");
    }

    public ToolResult ShowManual(ToolCall call, ToolArguments args)
    {
        var id = args.GetString("section_id");
        if (!manual.TryGetSection(id, out var section))
        {
            return ToolResult.Failure(call, $"Unknown section '{id}'. Use a section id from the context.");
        }

        scene.ShowManualSection(section);
        return ToolResult.Success(call, $"Showing [{section.Id}] {section.Title} on the panel.");
    }

    public ToolResult ReadTelemetry(ToolCall call, ToolArguments args)
    {
        var partId = args.GetString("part_id");
        if (partId != null && !catalog.Contains(partId))
        {
            return ToolResult.Failure(call, $"Unknown part '{partId}'.");
        }

        return ToolResult.Success(call, AssistantPrompt.DescribeTelemetry(telemetry));
    }

    /// <summary>
    /// Ties the voice loop to the procedure: the machine's gauge, not the user's words, decides. Within tolerance the
    /// reading is reported to the runner and the step completes (the step card advances); otherwise the value and the
    /// gap are returned so the assistant can say what to wait for or check.
    /// </summary>
    public ToolResult ReportReading(ToolCall call, ToolArguments args)
    {
        if (runner.State != RunnerState.Running)
        {
            return ToolResult.Failure(call, "No procedure is running, so there is no reading step to complete.");
        }

        var step = runner.CurrentStep;
        var number = runner.CurrentStepIndex + 1;
        if (step.Kind != StepKind.Measure)
        {
            return ToolResult.Failure(call, $"Step {number} '{step.Title}' is not a reading. {AssistantPrompt.Needs(runner, step, telemetry)}");
        }

        var value = telemetry[TelemetryChannel.Pressure];
        var stated = args.Has("stated_value") ? args.GetNumber("stated_value") : double.NaN;
        var said = !double.IsNaN(stated) && Math.Abs(stated - value) > step.Tolerance
            ? string.Format(CultureInfo.InvariantCulture, " The user said {0:0.#} {1}, but the gauge shows {2:0.0}.", stated, step.Unit, value)
            : string.Empty;
        if (Math.Abs(value - step.ExpectedValue) > step.Tolerance)
        {
            return ToolResult.Success(call, string.Format(CultureInfo.InvariantCulture,
                "Gauge reads {0:0.0} {1}; step {2} needs {3:0.#} ± {4:0.#} {1}, so it stays open.{5} Tell the user to keep watching the gauge or check the isolation.",
                value, step.Unit, number, step.ExpectedValue, step.Tolerance, said));
        }

        runner.Handle(InteractionEvent.Measured(scene.Now, value));
        var next = runner.State == RunnerState.Completed
            ? "The procedure is complete."
            : $"Now step {runner.CurrentStepIndex + 1} of {runner.Definition.Steps.Count}: {runner.CurrentStep.Title}. {AssistantPrompt.Needs(runner, runner.CurrentStep, telemetry)}";
        return ToolResult.Success(call, string.Format(CultureInfo.InvariantCulture,
            "Gauge reads {0:0.0} {1}, within {2:0.#} ± {3:0.#}. Step {4} is complete and the step card moved on.{5} {6}",
            value, step.Unit, step.ExpectedValue, step.Tolerance, number, said, next));
    }

    public ToolResult LogNote(ToolCall call, ToolArguments args)
    {
        scene.AddNote(args.GetString("text").Trim());
        return ToolResult.Success(call, "Noted in the maintenance log.");
    }

    public ToolResult SetLanguage(ToolCall call, ToolArguments args)
    {
        var language = args.GetString("language");
        LanguageChanged?.Invoke(language);
        return ToolResult.Success(call, language == "de" ? "Language set to German." : "Language set to English.");
    }

    public async Task<ToolResult> IdentifyViewAsync(ToolCall call, ToolArguments args, CancellationToken cancellationToken)
    {
        var answer = await scene.IdentifyViewAsync(cancellationToken);
        if (answer == null)
        {
            return ToolResult.Failure(call, "Camera identification is not available. Use the part the user is looking at from the context.");
        }

        return answer.Value.Identified
            ? ToolResult.Success(call, answer.Value.Text)
            : ToolResult.Failure(call, answer.Value.Text + " Use the part the user is looking at from the context, if any.");
    }

    private static string JoinIds<T>(IReadOnlyList<T> items, Func<T, string> id)
    {
        var ids = new List<string>(items.Count);
        foreach (var item in items)
        {
            ids.Add(id(item));
        }

        return string.Join(", ", ids);
    }
}
