using System.Collections.Generic;
using Fieldmate.Procedures;
using NUnit.Framework;
using static Fieldmate.Tests.EditMode.Procedures.ReliefValveProcedure;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>
/// #65: a part a safety rule guards is held still; trying it during a run is the violation, and nothing moves. #86: the
/// rules hold before and after a run too, and the restore runs cover → inlet → breaker.
/// </summary>
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
    public void Rules_hold_before_a_run_without_scoring_anything()
    {
        Assert.That(runner.Interlock(InletValve)?.Id, Is.EqualTo("loto_inlet"), "#86: a live machine is live before the run too");
        runner.Handle(InteractionEvent.Attempt(1, InletValve));
        Assert.That(log, Is.Empty, "outside a run an attempt is refused by the control, not recorded");
    }

    [Test]
    public void Outlet_is_held_whenever_the_pump_can_run_including_after_the_run()
    {
        var demo = Demo();
        Assert.That(demo.Interlock(Outlet)?.Id, Is.EqualTo("outlet_locked_out"), "before the run");

        double clock = 0;
        var lockedOut = Demo();
        ProcedureReplay.AdvanceTo(lockedOut, "close_inlet", ref clock);
        Assert.That(lockedOut.Interlock(Outlet), Is.Null, "locked out: the outlet may move");

        ProcedureReplay.AdvanceTo(demo, "verify_running", ref clock);
        Assert.That(demo.Interlock(Outlet)?.Id, Is.EqualTo("outlet_locked_out"), "breaker back on");
        demo.Handle(InteractionEvent.Measured(clock + 1, 4f));
        Assert.That(demo.State, Is.EqualTo(RunnerState.Completed));
        Assert.That(demo.Interlock(Outlet)?.Id, Is.EqualTo("outlet_locked_out"), "the device test: the outlet still turned after the run");

        demo.SetInitialState(Breaker, "locked"); // the router reports changes after a run this way
        Assert.That(demo.Interlock(Outlet), Is.Null, "the interlock follows the machine after a run");
    }

    [Test]
    public void Breaker_stays_held_while_the_outlet_is_closed_and_a_closed_outlet_can_always_be_reopened()
    {
        // Device test 2026-10-02 (#90): the outlet closed while locked out, power back on, 5.5 bar on the relief valve,
        // and the interlock then refused to reopen the outlet.
        var demo = Demo();
        double clock = 0;
        ProcedureReplay.AdvanceTo(demo, "power_on", ref clock);
        Assert.That(demo.Interlock(Outlet), Is.Null, "locked out: the outlet may be closed");
        demo.Handle(InteractionEvent.State(clock + 1, Outlet, "closed"));
        Assert.That(demo.Interlock(Breaker)?.Id, Is.EqualTo("outlet_before_power"), "no start against a closed outlet");

        demo.Handle(InteractionEvent.State(clock + 2, Outlet, "open"));
        Assert.That(demo.Interlock(Breaker), Is.Null);

        // Even with the pump running (state forced: the interlock would not let it get here), reopening is allowed.
        var running = Demo();
        running.SetInitialState(Outlet, "closed");
        Assert.That(running.Interlock(Outlet), Is.Null, "a closed outlet can be reopened while the pump runs");
        running.SetInitialState(Outlet, "open");
        Assert.That(running.Interlock(Outlet)?.Id, Is.EqualTo("outlet_locked_out"), "but an open one can't be throttled");
    }

    [Test]
    public void Restore_runs_cover_then_inlet_then_breaker()
    {
        var demo = Demo();
        double clock = 0;
        ProcedureReplay.AdvanceTo(demo, "refit_cover", ref clock);
        Assert.That(demo.Interlock(InletValve)?.Id, Is.EqualTo("cover_before_inlet"), "water into an open pump leaks out");
        Assert.That(demo.Interlock(Breaker)?.Id, Is.EqualTo("cover_before_power"));

        demo.Handle(InteractionEvent.State(clock + 1, Cover, "fitted"));
        Assert.That(demo.CurrentStep.Id, Is.EqualTo("open_inlet"));
        Assert.That(demo.Interlock(InletValve), Is.Null);
        Assert.That(demo.Interlock(Breaker)?.Id, Is.EqualTo("inlet_before_power"), "the pump must not run dry");

        demo.Handle(InteractionEvent.State(clock + 2, InletValve, "open"));
        Assert.That(demo.CurrentStep.Id, Is.EqualTo("power_on"));
        Assert.That(demo.Interlock(Breaker), Is.Null);

        demo.Handle(InteractionEvent.State(clock + 3, Breaker, "on"));
        Assert.That(demo.CurrentStep.Id, Is.EqualTo("verify_running"));
        Assert.That(demo.Violations, Is.Empty);
        Assert.That(demo.Errors, Is.Empty);
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
        runner = Demo(); // the shipped rules include cover_before_power
        runner.Start(0);
        runner.Handle(InteractionEvent.State(1, Breaker, "locked"));
        runner.Handle(InteractionEvent.State(2, Cover, "removed"));
        Assert.That(runner.Interlock(Breaker)?.Id, Is.EqualTo("cover_before_power"));

        runner.Handle(InteractionEvent.State(3, Cover, "fitted"));
        Assert.That(runner.Interlock(Breaker), Is.Null);
    }

    private const string Outlet = "outlet_valve";

    private static ProcedureRunner Demo()
    {
        var demo = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        DemoProcedures.ApplyInitialStates(demo);
        return demo;
    }

    [Test]
    public void Attempt_counts_as_a_physical_action()
    {
        Assert.That(InteractionEvent.Attempt(1, InletValve).IsPhysicalAction, Is.True);
        Assert.That(InteractionEvent.Attempt(1, InletValve).Kind, Is.EqualTo(InteractionKind.Attempted));
    }
}
