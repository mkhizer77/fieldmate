using System.IO;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#16: scripted answers keyed by the current step when the model is unreachable.</summary>
public class FallbackResponsesTests
{
    private FallbackResponses fallback;
    private ProcedureRunner runner;
    private TelemetryModel telemetry;

    [SetUp]
    public void SetUp()
    {
        var manual = MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));
        runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        telemetry = new TelemetryModel(FaultModel.CreateDefault());
        fallback = new FallbackResponses(manual, runner, telemetry);
    }

    [Test]
    public void Without_a_procedure_it_points_at_the_start_button()
    {
        var reply = fallback.Answer("what should I do now?");
        Assert.That(reply.Intent, Is.EqualTo("next"));
        Assert.That(reply.Text, Does.Contain("Pinch Start"));
        Assert.That(fallback.Answer("let's start").Text, Does.Contain("Pinch Start"), "it cannot start by voice while offline");
    }

    [Test]
    public void During_a_run_it_gives_the_current_step()
    {
        runner.Restart(1);
        var reply = fallback.Answer("what next");
        Assert.That(reply.Text, Does.StartWith("Step 1 of 8: Inspect the relief valve."));
        runner.Handle(InteractionEvent.Gaze(2, "relief_valve", 3f));
        Assert.That(fallback.Answer("anything at all").Text, Does.StartWith("Step 2 of 8: Lock out the main breaker."), "unknown questions still get the step");
    }

    [Test]
    public void Where_highlights_the_named_part()
    {
        var reply = fallback.Answer("Where is the relief valve?");
        Assert.That(reply.Intent, Is.EqualTo("where"));
        Assert.That(reply.HighlightPartId, Is.EqualTo("relief_valve"));
        Assert.That(reply.Text, Does.Contain("highlighted"));
    }

    [Test]
    public void Readings_why_and_safety_come_from_the_twin_and_the_manual()
    {
        Assert.That(fallback.Answer("what's the pressure?").Text, Does.Contain("pressure").And.Contain("bar"));
        var why = fallback.Answer("why is the pressure so high?");
        Assert.That(why.SectionId, Is.EqualTo("fault.overpressure"));
        Assert.That(why.HighlightPartId, Is.EqualTo("relief_valve"));
        Assert.That(fallback.Answer("is this safe?").SectionId, Is.EqualTo("safety.loto"));
    }
}
