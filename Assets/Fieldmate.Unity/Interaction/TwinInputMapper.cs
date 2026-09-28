using Fieldmate.Twin;

namespace Fieldmate.Interaction;

/// <summary>
/// How operating a part changes the simulation (design.md §5.6): the breaker powers the motor, the valves set the
/// openings. Normalized positions come from the controls: 0 is the start detent (breaker on, valves open).
/// </summary>
public static class TwinInputMapper
{
    public const string MainBreaker = "main_breaker";
    public const string InletValve = "inlet_valve";
    public const string OutletValve = "outlet_valve";

    public static MachineInputs Apply(MachineInputs inputs, string partId, string state, float normalized) => partId switch
    {
        MainBreaker => inputs.WithPowered(state == "on"),
        InletValve => inputs.WithInlet(1f - normalized),
        OutletValve => inputs.WithOutlet(1f - normalized),
        _ => inputs,
    };

    /// <summary>True for controls whose effect follows their position continuously (valves), not just named states.</summary>
    public static bool IsContinuous(string partId) => partId is InletValve or OutletValve;
}
