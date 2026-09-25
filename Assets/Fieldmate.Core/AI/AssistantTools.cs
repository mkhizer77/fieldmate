using System;
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

    /// <summary>Captures one camera frame and identifies the gazed part; null while vision is unavailable (M2).</summary>
    string IdentifyView();

    /// <summary>Scene clock in seconds, used to start procedures.</summary>
    double Now { get; }
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
        registry.SetExecutor(FieldmateTools.LogNote, new DelegateToolExecutor(LogNote));
        registry.SetExecutor(FieldmateTools.SetLanguage, new DelegateToolExecutor(SetLanguage));
        registry.SetExecutor(FieldmateTools.IdentifyView, new DelegateToolExecutor(IdentifyView));
    }

    public ToolResult HighlightPart(ToolCall call, ToolArguments args)
    {
        var id = args.GetString("part_id");
        if (!catalog.TryGet(id, out var part))
        {
            return ToolResult.Failure(call, $"Unknown part '{id}'. Known parts: {JoinIds(catalog.All, p => p.Id)}.");
        }

        return scene.TryHighlightPart(id)
            ? ToolResult.Success(call, $"Highlighted the {part.Name} for the user.")
            : ToolResult.Failure(call, $"The {part.Name} is not placed in the scene yet.");
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

    public ToolResult IdentifyView(ToolCall call, ToolArguments args)
    {
        var answer = scene.IdentifyView();
        return answer == null
            ? ToolResult.Failure(call, "Camera identification is not available yet. Use the part the user is looking at from the context.")
            : ToolResult.Success(call, answer);
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
