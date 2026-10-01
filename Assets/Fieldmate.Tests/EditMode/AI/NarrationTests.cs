using System.Collections.Generic;
using Fieldmate.AI;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#61: the lines the assistant says on its own, and the event log the model reads.</summary>
public class NarrationTests
{
    [Test]
    public void Step_done_sounds_like_a_colleague_not_a_counter()
    {
        var next = StepDefinition.Operate("lockout", "Lock out the main breaker", "main_breaker", "locked");
        Assert.That(Narration.StepDone(1, 8, next, "Pinch the red bar with both hands."),
            Is.EqualTo("Nice work. Now let's lock out the main breaker. Pinch the red bar with both hands."));
        Assert.That(Narration.StepDone(2, 8, next, null), Is.EqualTo("Good, that's done. Next, let's lock out the main breaker."), "varied");
        Assert.That(Narration.StepDone(7, 8, next, null), Does.Contain("Last step:"));
        Assert.That(Narration.StepDone(8, 8, null, null), Is.EqualTo("Perfect. That was the last one."));
        for (var i = 1; i <= 8; i++)
        {
            Assert.That(Narration.StepDone(i, 8, next, null), Does.Not.Contain("of 8").And.Not.Contain("Step "), "device test 2026-10-01: no status readout");
        }
    }

    [Test]
    public void Violation_holds_on_and_a_mistake_says_what_comes_first_without_blame()
    {
        var rule = new SafetyRule("loto_inlet", "Lock out the breaker before touching the inlet valve.", "inlet_valve", "main_breaker", "locked");
        Assert.That(Narration.Violation(rule), Is.EqualTo("Hold on. Lock out the breaker before touching the inlet valve."));
        var current = StepDefinition.Operate("lockout", "Lock out the main breaker", "main_breaker", "locked");
        var early = Narration.Mistake(new ProcedureError(3, "close_inlet", "'Close the inlet valve' done before 'Lock out the main breaker'."), 2, current, "Both hands on the bar.");
        Assert.That(early, Is.EqualTo("Careful, that one comes a bit later. First, let's lock out the main breaker. Both hands on the bar."));
        var reading = Narration.Mistake(new ProcedureError(3, "lockout", "Reported 2 bar, expected 0 ± 0.2 bar."), 2, current, null);
        Assert.That(reading, Does.StartWith("Hmm, not quite. Reported 2 bar"));
    }

    [Test]
    public void Completed_reads_the_score_like_a_person()
    {
        var clean = new ProcedureResult("p", 300, new double[0], new List<ProcedureError>(), new List<SafetyRule>(), 0, 96, true);
        Assert.That(Narration.Completed(clean), Is.EqualTo("All done, and you passed: 96 out of 100, not a single slip. The pump's back in service."));
        var rule = new SafetyRule("loto_inlet", "Lock out first.", "inlet_valve", "main_breaker", "locked");
        var failed = new ProcedureResult("p", 300, new double[0], new List<ProcedureError>(), new List<SafetyRule> { rule }, 0, 55, false);
        Assert.That(Narration.Completed(failed), Is.EqualTo("That's the job finished, but it didn't pass this time: 55 out of 100, one safety issue. Let's go over what happened."));
    }

    [Test]
    public void Event_log_keeps_the_newest_with_ages()
    {
        var log = new SceneEventLog(2);
        Assert.That(log.ToPromptText(10), Is.EqualTo("none yet."));
        log.Add(1, "a");
        log.Add(5, "b");
        log.Add(9.5, "c");
        Assert.That(log.Count, Is.EqualTo(2));
        Assert.That(log.ToPromptText(10), Is.EqualTo("5 s ago: b; just now: c."));
    }
}
