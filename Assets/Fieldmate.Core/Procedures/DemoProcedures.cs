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
            StepDefinition.Confirm("restore", "Restore and return to service"),
        },
        new[]
        {
            new SafetyRule("loto_inlet", "Lock out the breaker before touching the inlet valve.", "inlet_valve", "main_breaker", "locked"),
            new SafetyRule("loto_cover", "Lock out the breaker before opening the pump.", "pump_cover", "main_breaker", "locked"),
            new SafetyRule("isolate_seat", "Close the inlet before opening the relief valve seat.", "relief_valve_seat", "inlet_valve", "closed"),
        },
        timeLimitSeconds: 600f);
}
