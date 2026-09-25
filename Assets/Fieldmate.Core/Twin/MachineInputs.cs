using System;

namespace Fieldmate.Twin;

/// <summary>Operator-controlled state of the skid that drives the simulation.</summary>
public readonly struct MachineInputs : IEquatable<MachineInputs>
{
    public MachineInputs(bool powered, float inletOpening, float outletOpening, float load)
    {
        Powered = powered;
        InletOpening = Clamp01(inletOpening);
        OutletOpening = Clamp01(outletOpening);
        Load = Clamp01(load);
    }

    /// <summary>Normal operation: breaker closed, both valves open, half load.</summary>
    public static MachineInputs Running => new(true, 1f, 1f, 0.5f);

    /// <summary>Locked out and isolated: breaker open, both valves closed.</summary>
    public static MachineInputs Isolated => new(false, 0f, 0f, 0f);

    /// <summary>Main breaker closed (motor energised).</summary>
    public bool Powered { get; }

    /// <summary>Inlet valve opening, 0 (closed) to 1 (open).</summary>
    public float InletOpening { get; }

    /// <summary>Outlet valve opening, 0 (closed) to 1 (open).</summary>
    public float OutletOpening { get; }

    /// <summary>Hydraulic load, 0 to 1.</summary>
    public float Load { get; }

    public MachineInputs WithPowered(bool powered) => new(powered, InletOpening, OutletOpening, Load);
    public MachineInputs WithInlet(float opening) => new(Powered, opening, OutletOpening, Load);
    public MachineInputs WithOutlet(float opening) => new(Powered, InletOpening, opening, Load);
    public MachineInputs WithLoad(float load) => new(Powered, InletOpening, OutletOpening, load);

    public bool Equals(MachineInputs other) =>
        Powered == other.Powered && InletOpening.Equals(other.InletOpening) &&
        OutletOpening.Equals(other.OutletOpening) && Load.Equals(other.Load);

    public override bool Equals(object obj) => obj is MachineInputs other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Powered, InletOpening, OutletOpening, Load);

    public override string ToString() =>
        $"powered={Powered} inlet={InletOpening:0.##} outlet={OutletOpening:0.##} load={Load:0.##}";

    private static float Clamp01(float value) => float.IsNaN(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
}
