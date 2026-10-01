using System;
using System.Linq;

namespace Fieldmate.Procedures;

/// <summary>
/// Brings a runner to a step the way a user would (#11 play-from-step, #17 eval): starts it and completes every step
/// before the target with the interaction that step expects (meeting a safety rule's requirement first when the step's
/// part is guarded), so there are no errors or violations on the way.
/// </summary>
public static class ProcedureReplay
{
    public static void AdvanceTo(ProcedureRunner runner, string stepId, ref double clock)
    {
        if (runner == null)
        {
            throw new ArgumentNullException(nameof(runner));
        }

        if (!runner.Definition.Steps.Any(s => s.Id == stepId))
        {
            throw new ArgumentException($"Unknown step '{stepId}'.", nameof(stepId));
        }

        runner.Start(clock);
        while (runner.State == RunnerState.Running && runner.CurrentStep.Id != stepId)
        {
            var step = runner.CurrentStep;
            clock += 10d;
            foreach (var rule in runner.Definition.SafetyRules)
            {
                // A user satisfies a guard first (refits the cover before switching the breaker back on).
                if (rule.GuardedPartId == step.PartId && runner.GetPartState(rule.RequiredPartId) != rule.RequiredState)
                {
                    runner.Handle(InteractionEvent.State(clock, rule.RequiredPartId, rule.RequiredState));
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
