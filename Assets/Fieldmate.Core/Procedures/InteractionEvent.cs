namespace Fieldmate.Procedures;

public enum InteractionKind
{
    /// <summary>Continuous gaze on <see cref="InteractionEvent.PartId"/> for <see cref="InteractionEvent.Number"/> seconds.</summary>
    GazeDwell,

    /// <summary><see cref="InteractionEvent.PartId"/> changed to state <see cref="InteractionEvent.Value"/>.</summary>
    StateChanged,

    /// <summary>Tool <see cref="InteractionEvent.Value"/> placed in socket <see cref="InteractionEvent.PartId"/>.</summary>
    ToolSocketed,

    /// <summary>User reported reading <see cref="InteractionEvent.Number"/> (for the current Measure step).</summary>
    MeasurementReported,

    /// <summary>User acknowledged the current step.</summary>
    Confirmed,

    /// <summary>User asked the assistant for help (counts towards assistant reliance).</summary>
    HelpRequested,
}

/// <summary>
/// Something the user did, as reported by interactables, gaze, voice or UI. <see cref="Time"/> is seconds on any
/// monotonic clock; the runner never reads a clock itself, so recorded sequences replay deterministically.
/// </summary>
public readonly struct InteractionEvent
{
    public InteractionEvent(InteractionKind kind, double time, string partId = null, string value = null, float number = 0f)
    {
        Kind = kind;
        Time = time;
        PartId = partId;
        Value = value;
        Number = number;
    }

    public InteractionKind Kind { get; }
    public double Time { get; }
    public string PartId { get; }
    public string Value { get; }
    public float Number { get; }

    public static InteractionEvent Gaze(double time, string partId, float seconds) => new(InteractionKind.GazeDwell, time, partId, number: seconds);
    public static InteractionEvent State(double time, string partId, string state) => new(InteractionKind.StateChanged, time, partId, state);
    public static InteractionEvent Socketed(double time, string socketId, string toolId) => new(InteractionKind.ToolSocketed, time, socketId, toolId);
    public static InteractionEvent Measured(double time, float value) => new(InteractionKind.MeasurementReported, time, number: value);
    public static InteractionEvent Confirmed(double time) => new(InteractionKind.Confirmed, time);
    public static InteractionEvent Help(double time) => new(InteractionKind.HelpRequested, time);

    /// <summary>Events that act on a part (and can therefore break a safety rule).</summary>
    public bool IsPhysicalAction => Kind is InteractionKind.StateChanged or InteractionKind.ToolSocketed;

    public override string ToString() => $"{Kind}@{Time:0.##} part={PartId} value={Value} n={Number:0.##}";
}
