namespace Fieldmate.Twin;

/// <summary>Simulated sensor channels of the pump skid. Values double as array indices.</summary>
public enum TelemetryChannel
{
    Pressure = 0,
    Temperature = 1,
    Vibration = 2,
    Current = 3,
}

public enum ChannelStatus
{
    Normal,
    Warning,
    Alarm,
}

/// <summary>Unit, dynamics and alarm thresholds of one channel.</summary>
public readonly struct ChannelSpec
{
    public ChannelSpec(string unit, float timeConstantSeconds, float warning, float alarm)
    {
        Unit = unit;
        TimeConstantSeconds = timeConstantSeconds;
        Warning = warning;
        Alarm = alarm;
    }

    public string Unit { get; }

    /// <summary>First-order time constant: 63% of a step change is reached after this many seconds.</summary>
    public float TimeConstantSeconds { get; }

    public float Warning { get; }
    public float Alarm { get; }

    public ChannelStatus Classify(float value) =>
        value >= Alarm ? ChannelStatus.Alarm : value >= Warning ? ChannelStatus.Warning : ChannelStatus.Normal;
}
