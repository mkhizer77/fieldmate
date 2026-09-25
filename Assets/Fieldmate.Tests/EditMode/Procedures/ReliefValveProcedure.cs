using Fieldmate.Procedures;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>Test fixture shaped like the demo's relief-valve replacement (design.md §3).</summary>
internal static class ReliefValveProcedure
{
    public const string Breaker = "main_breaker";
    public const string InletValve = "inlet_valve";
    public const string Gauge = "pressure_gauge";
    public const string Cover = "pump_cover";
    public const string ReliefSeat = "relief_valve_seat";
    public const string Cartridge = "relief_cartridge";

    public static ProcedureDefinition Create(float timeLimitSeconds = 600f) => new(
        "relief_valve_replacement",
        "Replace relief valve cartridge",
        new[]
        {
            StepDefinition.Inspect("inspect", "Inspect the relief valve", "relief_valve", 1.5f),
            StepDefinition.Operate("lockout", "Lock out the main breaker", Breaker, "locked"),
            StepDefinition.Operate("close_inlet", "Close the inlet valve", InletValve, "closed"),
            StepDefinition.Measure("verify_zero", "Verify zero pressure", Gauge, 0f, 0.2f, "bar"),
            StepDefinition.Operate("remove_cover", "Remove the pump cover", Cover, "removed"),
            StepDefinition.Tool("replace", "Fit the new cartridge", ReliefSeat, Cartridge),
            StepDefinition.Confirm("restore", "Restore and return to service"),
        },
        new[]
        {
            new SafetyRule("loto_inlet", "Lock out the breaker before touching the inlet valve.", InletValve, Breaker, "locked"),
            new SafetyRule("loto_cover", "Lock out the breaker before opening the pump.", Cover, Breaker, "locked"),
            new SafetyRule("isolate_seat", "Close the inlet before opening the relief valve seat.", ReliefSeat, InletValve, "closed"),
        },
        timeLimitSeconds);

    /// <summary>The recorded happy path: one event per step, 10 s apart.</summary>
    public static InteractionEvent[] HappyPath() => new[]
    {
        InteractionEvent.Gaze(10, "relief_valve", 1.6f),
        InteractionEvent.State(20, Breaker, "locked"),
        InteractionEvent.State(30, InletValve, "closed"),
        InteractionEvent.Measured(40, 0.05f),
        InteractionEvent.State(50, Cover, "removed"),
        InteractionEvent.Socketed(60, ReliefSeat, Cartridge),
        InteractionEvent.Confirmed(70),
    };
}
