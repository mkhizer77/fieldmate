using System.IO;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Procedures;

public class ProcedurePresentationTests
{
    private static readonly ProcedureDefinition Demo = DemoProcedures.ReliefValveReplacement();

    private static StepDefinition Step(string id)
    {
        foreach (var step in Demo.Steps)
        {
            if (step.Id == id) return step;
        }

        throw new System.ArgumentException(id);
    }

    [Test]
    public void DwellTracker_AccumulatesOnOnePart_AndResetsOnAnother()
    {
        var dwell = new DwellTracker();
        dwell.Update("pressure_gauge", 0.5f);
        dwell.Update("pressure_gauge", 0.5f);
        Assert.That(dwell.Update("pressure_gauge", 0.5f), Is.EqualTo(1f), "the first frame on a part starts the clock");
        Assert.That(dwell.Update("relief_valve", 0.5f), Is.EqualTo(1f), "one frame elsewhere is forgiven (grace)");
        Assert.That(dwell.Update("relief_valve", 0.5f), Is.Zero, "past the grace the look has moved");
        Assert.That(dwell.Update(null, 0.5f), Is.Zero);
        Assert.That(dwell.Update(null, 0.5f), Is.Zero, "nothing under the gaze never accumulates");
    }

    [Test]
    public void Gauge_InTolerance_IsReported()
    {
        Assert.That(GaugeCheck.TryRead(Step("verify_zero"), 0.1f, out var hint), Is.True);
        Assert.That(hint, Is.Null);
        Assert.That(GaugeCheck.TryRead(Step("verify_running"), 4.4f, out _), Is.True);
    }

    [Test]
    public void Gauge_LookedAtTooEarly_IsAHintNotAnError()
    {
        Assert.That(GaugeCheck.TryRead(Step("verify_zero"), 3.2f, out var hint), Is.False);
        Assert.That(hint, Does.StartWith("Gauge reads 3.2 bar, not 0 yet"));
        Assert.That(GaugeCheck.TryRead(Step("verify_running"), 0f, out hint), Is.False);
        Assert.That(hint, Does.Contain("Is the inlet open"));
    }

    [Test]
    public void Gauge_OnlyReadsForMeasureSteps()
    {
        Assert.That(GaugeCheck.TryRead(Step("lockout"), 0f, out _), Is.False);
    }

    [Test]
    public void Instructions_UsePartNames_AndSayTwoHandsForTheBreaker()
    {
        var catalog = PartCatalog.FromManual(MachineManual.Parse(File.ReadAllText(
            Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json"))));
        Assert.That(StepInstructions.For(Step("lockout"), catalog), Does.Contain("both hands").And.Contain("LOCKED"));
        Assert.That(StepInstructions.For(Step("inspect"), catalog), Is.EqualTo("Look at the pressure relief valve for a moment."));
        Assert.That(StepInstructions.For(Step("replace"), catalog), Does.Contain("relief valve cartridge").And.Contain("relief valve seat"));
        Assert.That(StepInstructions.For(Step("remove_cover"), catalog), Does.Contain("pull it off"));
        Assert.That(StepInstructions.For(Step("verify_running"), catalog), Does.Contain("4 ± 1 bar"));
        Assert.That(StepInstructions.For(Step("lockout"), catalog, Fieldmate.XR.Modality.Controllers), Does.StartWith("Grip").And.Contain("both controllers"));
        Assert.That(StepInstructions.Short(Step("close_inlet")), Is.EqualTo("pinch · turn to CLOSED"));
    }

    [Test]
    public void DemoProcedure_EightSteps_CompleteWithoutErrors_InTheRightOrder()
    {
        var runner = new ProcedureRunner(Demo);
        runner.SetInitialState("main_breaker", "on");
        runner.SetInitialState("inlet_valve", "open");
        runner.SetInitialState("pump_cover", "fitted");
        ProcedureResult result = null;
        runner.ProcedureCompleted += r => result = r;
        runner.Start(0);

        runner.Handle(InteractionEvent.Gaze(1, "relief_valve", 2f));
        runner.Handle(InteractionEvent.State(2, "main_breaker", "off"));
        runner.Handle(InteractionEvent.State(3, "main_breaker", "locked"));
        runner.Handle(InteractionEvent.State(4, "inlet_valve", "closed"));
        runner.Handle(InteractionEvent.Measured(5, 0.05f));
        runner.Handle(InteractionEvent.State(6, "pump_cover", "removed"));
        runner.Handle(InteractionEvent.Socketed(7, "relief_valve_seat", "relief_cartridge"));
        runner.Handle(InteractionEvent.State(8, "pump_cover", "fitted"));
        runner.Handle(InteractionEvent.State(9, "inlet_valve", "open"));
        runner.Handle(InteractionEvent.State(10, "main_breaker", "off"));
        runner.Handle(InteractionEvent.State(11, "main_breaker", "on"));
        runner.Handle(InteractionEvent.Measured(12, 4.1f));

        Assert.That(result, Is.Not.Null, "all eight steps completed");
        Assert.That(Demo.Steps, Has.Count.EqualTo(8));
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Violations, Is.Empty);
        Assert.That(result.Passed, Is.True);
    }

    [Test]
    public void DemoProcedure_PowerWithTheCoverOff_IsAViolation()
    {
        var runner = new ProcedureRunner(Demo);
        runner.SetInitialState("pump_cover", "removed");
        runner.Start(0);

        runner.Handle(InteractionEvent.State(1, "main_breaker", "on"));

        Assert.That(runner.Violations, Has.Some.Matches<SafetyRule>(r => r.Id == "cover_before_power"));
    }
}
