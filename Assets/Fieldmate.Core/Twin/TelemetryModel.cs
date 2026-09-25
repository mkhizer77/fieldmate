using System;

namespace Fieldmate.Twin;

/// <summary>
/// First-order simulation of the pump skid's telemetry (design.md §5.6). Each channel relaxes towards a target set by the
/// <see cref="Inputs"/> and the active faults. <see cref="Step"/> uses the exact discrete solution, so results depend only
/// on elapsed time, not on how it is split into steps. Deterministic and allocation-free per step.
/// </summary>
public sealed class TelemetryModel
{
    /// <summary>Line pressure with both valves fully open, bar.</summary>
    public const float NominalPressure = 4f;

    /// <summary>The healthy relief valve caps line pressure here, bar.</summary>
    public const float ReliefSetPoint = 5.5f;

    public const float AmbientTemperature = 25f;

    /// <summary>Isolation needs line pressure below this, bar.</summary>
    public const float IsolationPressure = 0.2f;

    private static readonly ChannelSpec[] Specs =
    {
        new("bar", 0.8f, warning: 5f, alarm: 6f),
        new("°C", 20f, warning: 65f, alarm: 80f),
        new("mm/s", 0.5f, warning: 4.5f, alarm: 7.1f),
        new("A", 0.3f, warning: 11f, alarm: 13f),
    };

    private readonly float[] values = new float[ChannelCount];
    private readonly float[] targets = new float[ChannelCount];

    public TelemetryModel(FaultModel faults, MachineInputs initialInputs)
    {
        Faults = faults ?? throw new ArgumentNullException(nameof(faults));
        Inputs = initialInputs;
        Settle();
    }

    public TelemetryModel(FaultModel faults) : this(faults, MachineInputs.Running)
    {
    }

    public static int ChannelCount => 4;

    public FaultModel Faults { get; }

    /// <summary>Operator-controlled machine state. Changes take effect gradually through <see cref="Step"/>.</summary>
    public MachineInputs Inputs { get; set; }

    /// <summary>Simulated seconds since construction or the last <see cref="Settle"/>.</summary>
    public double ElapsedSeconds { get; private set; }

    /// <summary>Breaker open and line pressure bled off: safe to open the pump and repair.</summary>
    public bool IsIsolated => !Inputs.Powered && values[(int)TelemetryChannel.Pressure] < IsolationPressure;

    public float this[TelemetryChannel channel] => values[Index(channel)];

    public static ChannelSpec Spec(TelemetryChannel channel) => Specs[Index(channel)];

    public ChannelStatus Status(TelemetryChannel channel) => Specs[Index(channel)].Classify(values[Index(channel)]);

    /// <summary>The value the channel is heading towards under the current inputs and faults.</summary>
    public float Target(TelemetryChannel channel)
    {
        ComputeTargets();
        return targets[Index(channel)];
    }

    /// <summary>The worst status across all channels.</summary>
    public ChannelStatus WorstStatus()
    {
        var worst = ChannelStatus.Normal;
        for (var i = 0; i < ChannelCount; i++)
        {
            var status = Specs[i].Classify(values[i]);
            if (status > worst)
            {
                worst = status;
            }
        }

        return worst;
    }

    /// <summary>Advances the simulation by <paramref name="deltaSeconds"/> (≥ 0).</summary>
    public void Step(float deltaSeconds)
    {
        if (deltaSeconds < 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "Step needs a finite, non-negative delta.");
        }

        if (deltaSeconds == 0f)
        {
            return;
        }

        ComputeTargets();
        for (var i = 0; i < ChannelCount; i++)
        {
            var blend = 1f - MathF.Exp(-deltaSeconds / Specs[i].TimeConstantSeconds);
            values[i] += (targets[i] - values[i]) * blend;
        }

        ElapsedSeconds += deltaSeconds;
    }

    /// <summary>Jumps every channel to its target (scene start, after a reset).</summary>
    public void Settle()
    {
        ComputeTargets();
        Array.Copy(targets, values, ChannelCount);
        ElapsedSeconds = 0d;
    }

    /// <summary>Applies a repair action; succeeds only while <see cref="IsIsolated"/>.</summary>
    public RepairOutcome TryRepair(string repairActionId) => Faults.TryRepair(repairActionId, IsIsolated);

    private void ComputeTargets()
    {
        var inputs = Inputs;
        var effect = Faults.CombinedEffect();
        var running = inputs.Powered;

        // Pressure rises when the outlet is throttled; the relief valve caps it unless a fault defeats it.
        var pressure = running ? NominalPressure * inputs.InletOpening * (1f + 0.5f * (1f - inputs.OutletOpening)) : 0f;
        if (running)
        {
            pressure += effect.Pressure * inputs.InletOpening;
        }

        if (!effect.DefeatsReliefValve && pressure > ReliefSetPoint)
        {
            pressure = ReliefSetPoint;
        }

        targets[(int)TelemetryChannel.Pressure] = pressure;
        targets[(int)TelemetryChannel.Temperature] =
            AmbientTemperature + (running ? 20f * (0.5f + 0.5f * inputs.Load) + effect.Temperature : 0f);
        targets[(int)TelemetryChannel.Vibration] = running ? 2f + effect.Vibration : 0f;
        targets[(int)TelemetryChannel.Current] =
            running ? 6f + 4f * inputs.Load * inputs.InletOpening + effect.Current : 0f;
    }

    private static int Index(TelemetryChannel channel)
    {
        var index = (int)channel;
        if (index < 0 || index >= ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown telemetry channel.");
        }

        return index;
    }
}
