using System;
using System.Collections.Generic;

namespace Fieldmate.Procedures;

public enum RunnerState
{
    NotStarted,
    Running,
    Completed,
}

/// <summary>
/// Deterministic state machine that validates a procedure from <see cref="InteractionEvent"/>s (design.md §5.3).
/// Steps complete only from interaction state, never from a "next" button. Safety rules are checked on every physical
/// action; mistakes that don't endanger anyone are recorded as errors. The runner never reads a clock: all timing comes
/// from event timestamps, so recorded sequences replay exactly.
/// </summary>
public sealed class ProcedureRunner
{
    private readonly Dictionary<string, string> initialStates = new();
    private readonly Dictionary<string, string> partStates = new();
    private readonly List<ProcedureError> errors = new();
    private readonly List<SafetyRule> violations = new();
    private readonly double[] stepSeconds;

    private double startTime;
    private double stepStartTime;
    private int helpRequests;

    public ProcedureRunner(ProcedureDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        var problems = definition.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException($"Procedure '{definition.Id}' is invalid: {string.Join(" ", problems)}", nameof(definition));
        }

        stepSeconds = new double[definition.Steps.Count];
    }

    public event Action<int, StepDefinition> StepStarted;
    public event Action<int, StepDefinition, double> StepCompleted;
    public event Action<SafetyRule, InteractionEvent> ViolationRaised;
    public event Action<ProcedureError> ErrorRecorded;
    public event Action<ProcedureResult> ProcedureCompleted;

    public ProcedureDefinition Definition { get; }
    public RunnerState State { get; private set; }

    /// <summary>Index of the step in progress; equals the step count once completed; -1 before start.</summary>
    public int CurrentStepIndex { get; private set; } = -1;

    public StepDefinition CurrentStep =>
        State == RunnerState.Running ? Definition.Steps[CurrentStepIndex] : null;

    public IReadOnlyList<ProcedureError> Errors => errors;
    public IReadOnlyList<SafetyRule> Violations => violations;
    public int HelpRequests => helpRequests;
    public ProcedureResult Result { get; private set; }

    /// <summary>Known states of scene parts (e.g. breaker "on"), updated from StateChanged events.</summary>
    public IReadOnlyDictionary<string, string> PartStates => partStates;

    /// <summary>Declares a part's state at the start of every run (the scene's initial setup).</summary>
    public void SetInitialState(string partId, string state)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("Part id is required.", nameof(partId));
        }

        initialStates[partId] = state;
        if (State == RunnerState.NotStarted)
        {
            partStates[partId] = state;
        }
    }

    public string GetPartState(string partId) =>
        partId != null && partStates.TryGetValue(partId, out var state) ? state : null;

    public void Start(double time)
    {
        if (State == RunnerState.Running)
        {
            throw new InvalidOperationException("Procedure already running; use Restart.");
        }

        Reset();
        State = RunnerState.Running;
        startTime = time;
        BeginStep(0, time);
    }

    /// <summary>Discards the current run (errors, violations, part states) and starts over.</summary>
    public void Restart(double time)
    {
        State = RunnerState.NotStarted;
        Start(time);
    }

    /// <summary>Consumes one event. Returns true if it completed the current step.</summary>
    public bool Handle(in InteractionEvent e)
    {
        if (State != RunnerState.Running)
        {
            return false;
        }

        if (e.Kind == InteractionKind.HelpRequested)
        {
            helpRequests++;
            return false;
        }

        var violated = e.IsPhysicalAction && CheckSafety(e);
        if (e.Kind == InteractionKind.StateChanged && e.PartId != null)
        {
            partStates[e.PartId] = e.Value;
        }

        var step = Definition.Steps[CurrentStepIndex];
        if (Completes(step, e))
        {
            CompleteStep(e.Time);
            return true;
        }

        RecordMistake(step, e, violated);
        return false;
    }

    private bool Completes(StepDefinition step, in InteractionEvent e) => step.Kind switch
    {
        StepKind.Inspect => e.Kind == InteractionKind.GazeDwell && e.PartId == step.PartId && e.Number >= step.DwellSeconds,
        StepKind.Operate => e.Kind == InteractionKind.StateChanged && e.PartId == step.PartId && e.Value == step.TargetState,
        StepKind.Tool => e.Kind == InteractionKind.ToolSocketed && e.PartId == step.PartId && e.Value == step.ToolId,
        StepKind.Measure => e.Kind == InteractionKind.MeasurementReported &&
                            Math.Abs(e.Number - step.ExpectedValue) <= step.Tolerance,
        StepKind.Confirm => e.Kind == InteractionKind.Confirmed,
        _ => false,
    };

    private void RecordMistake(StepDefinition step, in InteractionEvent e, bool violated)
    {
        if (step.Kind == StepKind.Measure && e.Kind == InteractionKind.MeasurementReported)
        {
            AddError(e.Time, step.Id,
                $"Reported {e.Number:0.##} {step.Unit}, expected {step.ExpectedValue:0.##} ± {step.Tolerance:0.##} {step.Unit}.");
            return;
        }

        if (step.Kind == StepKind.Tool && e.Kind == InteractionKind.ToolSocketed && e.PartId == step.PartId)
        {
            AddError(e.Time, step.Id, $"Wrong tool '{e.Value}' in {step.PartId}; needs '{step.ToolId}'.");
            return;
        }

        // A physical action that completes a later step was done out of order. A safety violation already covers it.
        if (!violated && e.IsPhysicalAction)
        {
            for (var i = CurrentStepIndex + 1; i < Definition.Steps.Count; i++)
            {
                if (Completes(Definition.Steps[i], e))
                {
                    AddError(e.Time, Definition.Steps[i].Id, $"'{Definition.Steps[i].Title}' done before '{step.Title}'.");
                    return;
                }
            }
        }
    }

    private bool CheckSafety(in InteractionEvent e)
    {
        var violated = false;
        var rules = Definition.SafetyRules;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.GuardedPartId != e.PartId || GetPartState(rule.RequiredPartId) == rule.RequiredState)
            {
                continue;
            }

            violated = true;
            if (!violations.Contains(rule))
            {
                violations.Add(rule);
                ViolationRaised?.Invoke(rule, e);
            }
        }

        return violated;
    }

    private void AddError(double time, string stepId, string message)
    {
        var error = new ProcedureError(time, stepId, message);
        errors.Add(error);
        ErrorRecorded?.Invoke(error);
    }

    private void BeginStep(int index, double time)
    {
        CurrentStepIndex = index;
        stepStartTime = time;
        var step = Definition.Steps[index];
        StepStarted?.Invoke(index, step);

        // Validated by interaction state: a part already in its target state (operated early, which was recorded as
        // an out-of-order error) completes the step instead of forcing the user to undo and redo it.
        if (step.Kind == StepKind.Operate && GetPartState(step.PartId) == step.TargetState)
        {
            CompleteStep(time);
        }
    }

    private void CompleteStep(double time)
    {
        var index = CurrentStepIndex;
        var duration = Math.Max(0d, time - stepStartTime);
        stepSeconds[index] = duration;
        StepCompleted?.Invoke(index, Definition.Steps[index], duration);

        if (index + 1 < Definition.Steps.Count)
        {
            BeginStep(index + 1, time);
            return;
        }

        State = RunnerState.Completed;
        CurrentStepIndex = Definition.Steps.Count;
        var total = Math.Max(0d, time - startTime);
        var weights = Definition.Weights;
        var score = Scoring.Score(weights, total, Definition.TimeLimitSeconds, errors.Count, violations.Count, helpRequests);
        Result = new ProcedureResult(Definition.Id, total, (double[])stepSeconds.Clone(), errors.ToArray(),
            violations.ToArray(), helpRequests, score, Scoring.Passed(weights, score, violations.Count));
        ProcedureCompleted?.Invoke(Result);
    }

    private void Reset()
    {
        errors.Clear();
        violations.Clear();
        helpRequests = 0;
        Result = null;
        Array.Clear(stepSeconds, 0, stepSeconds.Length);
        partStates.Clear();
        foreach (var pair in initialStates)
        {
            partStates[pair.Key] = pair.Value;
        }
    }
}
