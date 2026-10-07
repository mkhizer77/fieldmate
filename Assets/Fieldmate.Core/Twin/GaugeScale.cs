using System;

namespace Fieldmate.Twin;

/// <summary>
/// The face of an analog dial (#88): a value range swept clockwise through <see cref="SweepDegrees"/>, centred on
/// twelve o'clock, with the needle pinned a little past either end like a real gauge's stop pin. Angles are degrees
/// clockwise from twelve o'clock as seen from the front.
/// </summary>
public readonly struct GaugeScale
{
    /// <summary>How far past either end of the scale the stop pin lets the needle go, degrees.</summary>
    public const float StopDegrees = 4f;

    public GaugeScale(float min, float max, float sweepDegrees)
    {
        if (max <= min)
        {
            throw new ArgumentException("The scale needs max > min.", nameof(max));
        }

        Min = min;
        Max = max;
        SweepDegrees = sweepDegrees;
    }

    /// <summary>The discharge pressure gauge: 0–10 bar over 270°, the fault's 6.8 bar well inside the red.</summary>
    public static GaugeScale Pressure => new(0f, 10f, 270f);

    public float Min { get; }
    public float Max { get; }
    public float SweepDegrees { get; }

    /// <summary>Needle angle for <paramref name="value"/>: -sweep/2 at <see cref="Min"/>, +sweep/2 at <see cref="Max"/>.</summary>
    public float Angle(float value)
    {
        var t = (value - Min) / (Max - Min);
        var angle = -SweepDegrees * 0.5f + t * SweepDegrees;
        var limit = SweepDegrees * 0.5f + StopDegrees;
        return Math.Max(-limit, Math.Min(limit, angle));
    }
}

/// <summary>A coloured arc on a dial, from <see cref="From"/> to <see cref="To"/> in the scale's units.</summary>
public readonly struct GaugeBand
{
    public GaugeBand(float from, float to, ChannelStatus status)
    {
        From = from;
        To = to;
        Status = status;
    }

    public float From { get; }
    public float To { get; }
    public ChannelStatus Status { get; }

    /// <summary>
    /// The pressure gauge's bands: green where the pump normally runs (nominal ± 1 bar, what "verify running pressure"
    /// accepts), amber from the channel's warning level, red from its alarm level to the end of the scale.
    /// </summary>
    public static GaugeBand[] Pressure()
    {
        var spec = TelemetryModel.Spec(TelemetryChannel.Pressure);
        var scale = GaugeScale.Pressure;
        return new[]
        {
            new GaugeBand(TelemetryModel.NominalPressure - 1f, spec.Warning, ChannelStatus.Normal),
            new GaugeBand(spec.Warning, spec.Alarm, ChannelStatus.Warning),
            new GaugeBand(spec.Alarm, scale.Max, ChannelStatus.Alarm),
        };
    }
}
