using System.Collections.Generic;
using Fieldmate.AI;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#61: the lines the assistant says on its own, and the event log the model reads.</summary>
public class NarrationTests
{
    [Test]
    public void Step_done_names_the_next_step_and_its_instruction()
    {
        var next = StepDefinition.Operate("lockout", "Lock out the main breaker", "main_breaker", "locked");
        Assert.That(Narration.StepDone(1, 8, next, "Pinch the red bar with both hands."),
            Is.EqualTo("Step 1 done. Next, step 2 of 8: Lock out the main breaker. Pinch the red bar with both hands."));
        Assert.That(Narration.StepDone(8, 8, null, null), Is.EqualTo("Step 8 done."));
    }

    [Test]
    public void Violation_and_mistake_say_stop_and_what_comes_first()
    {
        var rule = new SafetyRule("loto_inlet", "Lock out the breaker before touching the inlet valve.", "inlet_valve", "main_breaker", "locked");
        Assert.That(Narration.Violation(rule), Is.EqualTo("Stop. Lock out the breaker before touching the inlet valve."));
        var current = StepDefinition.Operate("lockout", "Lock out the main breaker", "main_breaker", "locked");
        var text = Narration.Mistake(new ProcedureError(3, "close_inlet", "'Close the inlet valve' done before 'Lock out the main breaker'."), 2, current, "Both hands on the bar.");
        Assert.That(text, Is.EqualTo("'Close the inlet valve' done before 'Lock out the main breaker'. First, step 2: Lock out the main breaker. Both hands on the bar."));
    }

    [Test]
    public void Completed_reads_the_score()
    {
        var result = new ProcedureResult("p", 300, new double[0], new List<ProcedureError>(), new List<SafetyRule>(), 0, 96, true);
        Assert.That(Narration.Completed(result), Is.EqualTo("Procedure complete, passed. Score 96 out of 100, 0 errors, 0 safety violations."));
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
