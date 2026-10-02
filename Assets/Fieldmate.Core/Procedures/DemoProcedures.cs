namespace Fieldmate.Procedures;

/// <summary>
/// The demo's relief-valve replacement (design.md §3), with step and part ids matching manual.json. Authored in code
/// until the ScriptableObject content pipeline (#11/#12) replaces it.
/// </summary>
public static class DemoProcedures
{
    public const string ReliefValveReplacementId = "relief_valve_replacement";

    public static ProcedureDefinition ReliefValveReplacement() => new(
        ReliefValveReplacementId,
        "Replace the relief valve cartridge",
        new[]
        {
            StepDefinition.Inspect("inspect", "Inspect the relief valve", "relief_valve", 1.5f),
            StepDefinition.Operate("lockout", "Lock out the main breaker", "main_breaker", "locked"),
            StepDefinition.Operate("close_inlet", "Close the inlet valve", "inlet_valve", "closed"),
            StepDefinition.Measure("verify_zero", "Verify zero pressure on the gauge", "pressure_gauge", 0f, 0.2f, "bar"),
            StepDefinition.Operate("remove_cover", "Remove the pump access cover", "pump_cover", "removed"),
            StepDefinition.Tool("replace", "Fit the new relief cartridge", "relief_valve_seat", "relief_cartridge"),
            // Restore in three steps, in this order (device test 2026-10-02): water before the cover is back leaks out of the
            // open pump, and power before the water is back runs the pump dry.
            StepDefinition.Operate("refit_cover", "Refit the pump access cover", "pump_cover", "fitted"),
            StepDefinition.Operate("open_inlet", "Open the inlet valve", "inlet_valve", "open"),
            StepDefinition.Operate("power_on", "Switch the main breaker back on", "main_breaker", "on"),
            StepDefinition.Measure("verify_running", "Verify running pressure is back in the green band", "pressure_gauge", 4f, 1f, "bar"),
        },
        new[]
        {
            new SafetyRule("loto_inlet", "Lock out the breaker before touching the inlet valve.", "inlet_valve", "main_breaker", "locked"),
            new SafetyRule("loto_cover", "Lock out the breaker before opening the pump.", "pump_cover", "main_breaker", "locked"),
            new SafetyRule("isolate_seat", "Close the inlet before opening the relief valve seat.", "relief_valve_seat", "inlet_valve", "closed"),
            new SafetyRule("cover_before_power", "Refit the pump cover before switching the breaker.", "main_breaker", "pump_cover", "fitted"),
            new SafetyRule("cover_before_inlet", "Refit the pump cover before opening the inlet, or water leaks out of the pump.", "inlet_valve", "pump_cover", "fitted"),
            new SafetyRule("inlet_before_power", "Open the inlet before switching the breaker, or the pump runs dry.", "main_breaker", "inlet_valve", "open"),
            // Throttling the discharge of a running pump spikes the line pressure: the outlet only moves while locked out.
            new SafetyRule("outlet_locked_out", "Lock out the breaker before touching the outlet valve; throttling it while the pump runs spikes the pressure.", "outlet_valve", "main_breaker", "locked"),
        },
        timeLimitSeconds: 600f);
}
