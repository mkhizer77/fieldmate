using System.Collections.Generic;
using Fieldmate.Procedures;
using NUnit.Framework;
using static Fieldmate.Tests.EditMode.Procedures.ReliefValveProcedure;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>#65: a part a safety rule guards is held still during a run; trying it is the violation, and nothing moves.</summary>
public class InterlockTests
{
    private ProcedureRunner runner;
    private List<string> log;

    [SetUp]
    public void SetUp()
    {
        runner = new ProcedureRunner(Create());
        runner.SetInitialState(Breaker, "on");
        runner.SetInitialState(InletValve, "open");
        runner.SetInitialState(Cover, "fitted");
        log = new List<string>();
        runner.ViolationRaised += (rule, e) => log.Add($"violation {rule.Id}");
        runner.AttemptRefused += (rule, e) => log.Add($"refused {rule.Id} {e.PartId}");
        runner.StepCompleted += (i, s, d) => log.Add($"done {s.Id}");
    }

    [Test]
    public void Nothing_is_held_outside_a_run()
    {
        Assert.That(runner.Interlock(InletValve), Is.Null);
        runner.Handle(InteractionEvent.Attempt(1, InletValve));
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void Inlet_and_cover_are_held_until_the_breaker_is_locked()
    {
        runner.Start(0);
        Assert.That(runner.Interlock(InletValve)?.Id, Is.EqualTo("loto_inlet"));
        Assert.That(runner.Interlock(Cover)?.Id, Is.EqualTo("loto_cover"));
        Assert.That(runner.Interlock(ReliefSeat)?.Id, Is.EqualTo("isolate_seat"));
        Assert.That(runner.Interlock(Breaker), Is.Null, "the breaker is free while the cover is fitted");
        Assert.That(runner.Interlock(Gauge), Is.Null, "unguarded parts are never held");

        runner.Handle(InteractionEvent.State(5, Breaker, "locked"));
        Assert.That(runner.Interlock(InletValve), Is.Null);
        Assert.That(runner.Interlock(Cover), Is.Null);
        Assert.That(runner.Interlock(ReliefSeat)?.Id, Is.EqualTo("isolate_seat"), "the seat still needs the inlet closed");
    }

    [Test]
    public void An_attempt_is_the_violation_once_and_a_refusal_every_time_and_moves_nothing()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.Attempt(3, InletValve));
        runner.Handle(InteractionEvent.Attempt(4, InletValve));

        Assert.That(log, Is.EqualTo(new[]
        {
            "violation loto_inlet", "refused loto_inlet inlet_valve", "refused loto_inlet inlet_valve",
        }));
        Assert.That(runner.GetPartState(InletValve), Is.EqualTo("open"));
        Assert.That(runner.Errors, Is.Empty, "the refusal is not also an out-of-order error");
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("inspect"));
    }

    [Test]
    public void Cover_off_holds_the_breaker_so_power_cannot_come_back_on()
    {
        runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement()); // the shipped rules include cover_before_power
        runner.SetInitialState(Breaker, "on");
        runner.SetInitialState(Cover, "fitted");
        runner.Start(0);
        runner.Handle(InteractionEvent.State(1, Breaker, "locked"));
        runner.Handle(InteractionEvent.State(2, Cover, "removed"));
        Assert.That(runner.Interlock(Breaker)?.Id, Is.EqualTo("cover_before_power"));

        runner.Handle(InteractionEvent.State(3, Cover, "fitted"));
        Assert.That(runner.Interlock(Breaker), Is.Null);
    }

    [Test]
    public void Attempt_counts_as_a_physical_action()
    {
        Assert.That(InteractionEvent.Attempt(1, InletValve).IsPhysicalAction, Is.True);
        Assert.That(InteractionEvent.Attempt(1, InletValve).Kind, Is.EqualTo(InteractionKind.Attempted));
    }
}
