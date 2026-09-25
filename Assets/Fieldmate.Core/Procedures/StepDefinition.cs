using System;

namespace Fieldmate.Procedures;

/// <summary>How a step is validated (design.md §5.3).</summary>
public enum StepKind
{
    /// <summary>Gaze dwell on a part for at least <see cref="StepDefinition.DwellSeconds"/>.</summary>
    Inspect,

    /// <summary>An interactable part reaches <see cref="StepDefinition.TargetState"/>.</summary>
    Operate,

    /// <summary>Tool <see cref="StepDefinition.ToolId"/> is placed in socket <see cref="StepDefinition.PartId"/>.</summary>
    Tool,

    /// <summary>A reading of <see cref="StepDefinition.PartId"/> is reported within tolerance of the expected value.</summary>
    Measure,

    /// <summary>Explicit acknowledgement by voice or UI.</summary>
    Confirm,
}

/// <summary>One validated step of a procedure. Create with the static factories.</summary>
public sealed class StepDefinition
{
    private StepDefinition(string id, string title, StepKind kind, string partId)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Step id is required.", nameof(id)) : id;
        Title = title ?? id;
        Kind = kind;
        PartId = partId;
    }

    public string Id { get; }
    public string Title { get; }
    public StepKind Kind { get; }

    /// <summary>Inspected, operated or measured part; the socket for <see cref="StepKind.Tool"/>; null for Confirm.</summary>
    public string PartId { get; }

    public float DwellSeconds { get; private set; }
    public string TargetState { get; private set; }
    public string ToolId { get; private set; }
    public float ExpectedValue { get; private set; }
    public float Tolerance { get; private set; }
    public string Unit { get; private set; }

    public static StepDefinition Inspect(string id, string title, string partId, float dwellSeconds) =>
        new(id, title, StepKind.Inspect, RequirePart(partId))
        {
            DwellSeconds = dwellSeconds > 0f ? dwellSeconds : throw new ArgumentOutOfRangeException(nameof(dwellSeconds)),
        };

    public static StepDefinition Operate(string id, string title, string partId, string targetState) =>
        new(id, title, StepKind.Operate, RequirePart(partId))
        {
            TargetState = string.IsNullOrWhiteSpace(targetState) ? throw new ArgumentException("Target state is required.", nameof(targetState)) : targetState,
        };

    public static StepDefinition Tool(string id, string title, string socketId, string toolId) =>
        new(id, title, StepKind.Tool, RequirePart(socketId))
        {
            ToolId = string.IsNullOrWhiteSpace(toolId) ? throw new ArgumentException("Tool id is required.", nameof(toolId)) : toolId,
        };

    public static StepDefinition Measure(string id, string title, string partId, float expected, float tolerance, string unit) =>
        new(id, title, StepKind.Measure, RequirePart(partId))
        {
            ExpectedValue = expected,
            Tolerance = tolerance >= 0f ? tolerance : throw new ArgumentOutOfRangeException(nameof(tolerance)),
            Unit = unit ?? string.Empty,
        };

    public static StepDefinition Confirm(string id, string title) => new(id, title, StepKind.Confirm, null);

    public override string ToString() => $"{Kind} '{Id}'";

    private static string RequirePart(string partId) =>
        string.IsNullOrWhiteSpace(partId) ? throw new ArgumentException("Part id is required.", nameof(partId)) : partId;
}
