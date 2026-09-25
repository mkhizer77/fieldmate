using System;
using System.Collections.Generic;
using System.Linq;
using Fieldmate.Procedures;
using NUnit.Framework;
using static Fieldmate.Tests.EditMode.Procedures.ReliefValveProcedure;

namespace Fieldmate.Tests.EditMode.Procedures;

public class ProcedureRunnerTests
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
        runner.StepStarted += (i, s) => log.Add($"start {s.Id}");
        runner.StepCompleted += (i, s, d) => log.Add($"done {s.Id} {d:0}");
        runner.ViolationRaised += (rule, e) => log.Add($"violation {rule.Id}");
        runner.ErrorRecorded += error => log.Add($"error {error.StepId}");
        runner.ProcedureCompleted += result => log.Add($"completed {result.Score}");
    }

    private void Replay(IEnumerable<InteractionEvent> events)
    {
        foreach (var e in events)
        {
            runner.Handle(e);
        }
    }

    [Test]
    public void HappyPath_CompletesAllStepsWithFullScore()
    {
        runner.Start(0);
        Replay(HappyPath());

        Assert.That(runner.State, Is.EqualTo(RunnerState.Completed));
        Assert.That(runner.CurrentStep, Is.Null);
        var result = runner.Result;
        Assert.That(result.Score, Is.EqualTo(100));
        Assert.That(result.Passed, Is.True);
        Assert.That(result.TotalSeconds, Is.EqualTo(70));
        Assert.That(result.StepSeconds, Is.EqualTo(new double[] { 10, 10, 10, 10, 10, 10, 10 }));
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Violations, Is.Empty);
        Assert.That(log.First(), Is.EqualTo("start inspect"));
        Assert.That(log.Last(), Is.EqualTo("completed 100"));
    }

    [Test]
    public void SkippedLockout_RaisesViolation_AndFailsTheRun()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.Gaze(5, "relief_valve", 2f));
        runner.Handle(InteractionEvent.State(8, InletValve, "closed")); // before lockout

        Assert.That(log, Does.Contain("violation loto_inlet"));
        Assert.That(runner.Errors, Is.Empty, "the violation already covers the out-of-order action");

        runner.Handle(InteractionEvent.State(10, Breaker, "locked"));
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("verify_zero"), "inlet already closed, so that step completes on arrival");

        Replay(new[]
        {
            InteractionEvent.Measured(20, 0f),
            InteractionEvent.State(30, Cover, "removed"),
            InteractionEvent.Socketed(40, ReliefSeat, Cartridge),
            InteractionEvent.Confirmed(50),
        });

        Assert.That(runner.Result.Violations.Select(v => v.Id), Is.EqualTo(new[] { "loto_inlet" }));
        Assert.That(runner.Result.Score, Is.EqualTo(75));
        Assert.That(runner.Result.Passed, Is.False, "any violation fails the run");
    }

    [Test]
    public void Violation_IsRaisedOncePerRule()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.State(1, InletValve, "closed"));
        runner.Handle(InteractionEvent.State(2, InletValve, "open"));
        runner.Handle(InteractionEvent.State(3, InletValve, "closed"));

        Assert.That(log.Count(l => l == "violation loto_inlet"), Is.EqualTo(1));
        Assert.That(runner.Violations, Has.Count.EqualTo(1));
    }

    [Test]
    public void SafetyRules_AreEvaluatedOnToolEventsToo()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.Socketed(1, ReliefSeat, Cartridge));

        Assert.That(log, Does.Contain("violation isolate_seat"));
    }

    [Test]
    public void Restart_ResetsStepsErrorsViolationsAndPartStates()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.State(1, InletValve, "closed"));
        runner.Handle(InteractionEvent.Help(2));

        runner.Restart(100);

        Assert.That(runner.State, Is.EqualTo(RunnerState.Running));
        Assert.That(runner.CurrentStepIndex, Is.Zero);
        Assert.That(runner.Violations, Is.Empty);
        Assert.That(runner.HelpRequests, Is.Zero);
        Assert.That(runner.GetPartState(InletValve), Is.EqualTo("open"));

        Replay(HappyPath().Select(e => new InteractionEvent(e.Kind, e.Time + 100, e.PartId, e.Value, e.Number)));
        Assert.That(runner.Result.Score, Is.EqualTo(100));
        Assert.That(runner.Result.TotalSeconds, Is.EqualTo(70));
    }

    [Test]
    public void Restart_AfterCompletion_StartsANewRun()
    {
        runner.Start(0);
        Replay(HappyPath());
        runner.Restart(200);

        Assert.That(runner.State, Is.EqualTo(RunnerState.Running));
        Assert.That(runner.Result, Is.Null);
    }

    [Test]
    public void Inspect_NeedsEnoughDwellOnTheRightPart()
    {
        runner.Start(0);

        Assert.That(runner.Handle(InteractionEvent.Gaze(1, "relief_valve", 1f)), Is.False);
        Assert.That(runner.Handle(InteractionEvent.Gaze(2, "motor", 5f)), Is.False);
        Assert.That(runner.Handle(InteractionEvent.Gaze(3, "relief_valve", 1.5f)), Is.True);
        Assert.That(runner.Errors, Is.Empty, "looking around is not a mistake");
    }

    [Test]
    public void Measure_OutOfTolerance_RecordsErrorThenAcceptsCorrectReading()
    {
        AdvanceTo("verify_zero");

        Assert.That(runner.Handle(InteractionEvent.Measured(50, 3.9f)), Is.False);
        Assert.That(runner.Errors.Single().Message, Does.Contain("expected 0 ± 0.2 bar"));
        Assert.That(runner.Handle(InteractionEvent.Measured(51, -0.1f)), Is.True);
    }

    [Test]
    public void Tool_WrongToolInSocket_IsAnError()
    {
        AdvanceTo("replace");

        Assert.That(runner.Handle(InteractionEvent.Socketed(80, ReliefSeat, "wrench")), Is.False);
        Assert.That(runner.Errors.Single().StepId, Is.EqualTo("replace"));
        Assert.That(runner.Handle(InteractionEvent.Socketed(81, ReliefSeat, Cartridge)), Is.True);
    }

    [Test]
    public void OutOfOrderAction_WithoutRule_IsAnError()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.State(1, Breaker, "locked")); // still on the inspect step

        Assert.That(runner.Errors.Single().StepId, Is.EqualTo("lockout"));
        Assert.That(runner.Violations, Is.Empty);
    }

    [Test]
    public void Confirm_CompletesOnAcknowledgementOnly()
    {
        AdvanceTo("restore");

        Assert.That(runner.Handle(InteractionEvent.Gaze(90, "motor", 3f)), Is.False);
        Assert.That(runner.Handle(InteractionEvent.Confirmed(91)), Is.True);
        Assert.That(runner.State, Is.EqualTo(RunnerState.Completed));
    }

    [Test]
    public void HelpRequests_AreCountedAndScored()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.Help(1));
        runner.Handle(InteractionEvent.Help(2));
        Replay(HappyPath());

        Assert.That(runner.Result.HelpRequests, Is.EqualTo(2));
        Assert.That(runner.Result.Score, Is.EqualTo(96));
        Assert.That(runner.Result.Passed, Is.True);
    }

    [Test]
    public void Events_AreIgnoredBeforeStartAndAfterCompletion()
    {
        Assert.That(runner.Handle(InteractionEvent.Confirmed(0)), Is.False);
        Assert.That(runner.CurrentStepIndex, Is.EqualTo(-1));

        runner.Start(0);
        Replay(HappyPath());
        Assert.That(runner.Handle(InteractionEvent.Help(99)), Is.False);
        Assert.That(runner.Result.HelpRequests, Is.Zero);
    }

    [Test]
    public void Start_WhileRunning_Throws()
    {
        runner.Start(0);

        Assert.Throws<InvalidOperationException>(() => runner.Start(1));
    }

    [Test]
    public void PartStates_TrackInitialAndChangedStates()
    {
        runner.Start(0);
        runner.Handle(InteractionEvent.State(1, "outlet_valve", "closed"));

        Assert.That(runner.PartStates[Breaker], Is.EqualTo("on"));
        Assert.That(runner.GetPartState("outlet_valve"), Is.EqualTo("closed"));
        Assert.That(runner.GetPartState("unknown"), Is.Null);
        Assert.That(runner.GetPartState(null), Is.Null);
        Assert.Throws<ArgumentException>(() => runner.SetInitialState(" ", "x"));
    }

    [Test]
    public void Constructor_RejectsInvalidDefinitions()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcedureRunner(null));
        Assert.Throws<ArgumentException>(() => new ProcedureRunner(new ProcedureDefinition("empty", "Empty", Array.Empty<StepDefinition>())));
    }

    private void AdvanceTo(string stepId)
    {
        runner.Start(0);
        foreach (var e in HappyPath())
        {
            if (runner.CurrentStep.Id == stepId)
            {
                return;
            }

            runner.Handle(e);
        }
    }
}
