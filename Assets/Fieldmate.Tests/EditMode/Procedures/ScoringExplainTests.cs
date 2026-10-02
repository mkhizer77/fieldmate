using System.Collections.Generic;
using Fieldmate.AI;
using Fieldmate.Procedures;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>Device test 2026-10-01: "86 out of 100" and the mate couldn't say why. The breakdown is spelled out.</summary>
public class ScoringExplainTests
{
    [Test]
    public void A_clean_run_says_no_deductions()
    {
        var result = new ProcedureResult("p", 173, new double[0], new List<ProcedureError>(), new List<SafetyRule>(), 0, 100, true);
        Assert.That(Scoring.Explain(result, ScoringWeights.Default, 600f),
            Is.EqualTo("score 100/100, passed; took 2 min 53 s, limit 10 min 0 s; no deductions."));
    }

    [Test]
    public void Every_deduction_is_named_with_its_points()
    {
        var errors = new List<ProcedureError> { new(10, "close_inlet", "'Close the inlet valve' done before 'Lock out the main breaker'.") };
        var result = new ProcedureResult("p", 700, new double[0], errors, new List<SafetyRule>(), 2, 78, true);
        var text = Scoring.Explain(result, ScoringWeights.Default, 600f);
        Assert.That(text, Does.StartWith("score 78/100, passed; took 11 min 40 s, limit 10 min 0 s; 100, "));
        Assert.That(text, Does.Contain("1 mistake -5 ('Close the inlet valve' done before 'Lock out the main breaker'.)"));
        Assert.That(text, Does.Contain("2 questions to the assistant during the run -4 (2 each)"));
        Assert.That(text, Does.Contain("1.7 min over the time limit -8"));
    }

    [Test]
    public void A_violation_explains_why_it_failed()
    {
        var rule = new SafetyRule("loto_inlet", "Lock out the breaker before touching the inlet valve.", "inlet_valve", "main_breaker", "locked");
        var result = new ProcedureResult("p", 300, new double[0], new List<ProcedureError>(), new List<SafetyRule> { rule }, 0, 75, false);
        var text = Scoring.Explain(result, ScoringWeights.Default, 600f);
        Assert.That(text, Does.Contain("not passed (any safety violation fails the run)"));
        Assert.That(text, Does.Contain("1 safety violation -25 (Lock out the breaker before touching the inlet valve.)"));
    }

    [Test]
    public void The_assistant_context_carries_the_breakdown_after_a_run()
    {
        var runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        runner.SetInitialState("main_breaker", "on");
        runner.SetInitialState("inlet_valve", "open");
        runner.SetInitialState("pump_cover", "fitted");
        var clock = 0d;
        ProcedureReplayForTests.Complete(runner, ref clock);
        Assert.That(runner.State, Is.EqualTo(RunnerState.Completed));
        Assert.That(AssistantPrompt.DescribeProcedure(runner), Does.Contain("completed: score 100/100, passed;").And.Contain("no deductions"));
    }
}

/// <summary>Completes every step the way a user would (like ProcedureReplay, which lands with #78).</summary>
internal static class ProcedureReplayForTests
{
    public static void Complete(ProcedureRunner runner, ref double clock)
    {
        runner.Start(clock);
        while (runner.State == RunnerState.Running)
        {
            var step = runner.CurrentStep;
            clock += 10d;
            foreach (var rule in runner.Definition.SafetyRules)
            {
                if (rule.GuardedPartId == step.PartId && runner.GetPartState(rule.RequiredPartId) != rule.RequiredState)
                {
                    runner.Handle(InteractionEvent.State(clock, rule.RequiredPartId, rule.RequiredState)); // e.g. refit the cover first
                }
            }

            runner.Handle(step.Kind switch
            {
                StepKind.Inspect => InteractionEvent.Gaze(clock, step.PartId, step.DwellSeconds + 0.5f),
                StepKind.Operate => InteractionEvent.State(clock, step.PartId, step.TargetState),
                StepKind.Tool => InteractionEvent.Socketed(clock, step.PartId, step.ToolId),
                StepKind.Measure => InteractionEvent.Measured(clock, step.ExpectedValue),
                _ => InteractionEvent.Confirmed(clock),
            });
        }
    }
}
