using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.XR;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Makes the assistant speak on its own (#61): a completed step announces the next one, a safety violation, a part
    /// the interlock holds again, an undone step or an out-of-order action interrupts whatever is playing (#90), the
    /// debrief reads the score. Every event also goes into the session's scene log so the model knows what just
    /// happened when the user asks.
    /// </summary>
    public sealed class ProactiveNarrator : MonoBehaviour
    {
        [SerializeField] private MachineServices machine;
        [SerializeField] private VoiceLoop loop;

        private readonly SafetyLineGate safetyLines = new();

        public void Configure(MachineServices services, VoiceLoop voiceLoop)
        {
            machine = services;
            loop = voiceLoop;
        }

        private void Start()
        {
            var runner = machine.Runner;
            runner.StepCompleted += OnStepCompleted;
            runner.ViolationRaised += OnViolation;
            runner.AttemptRefused += OnRefused;
            runner.StepUndone += OnUndone;
            runner.StepStarted += OnStepStarted;
            runner.ErrorRecorded += OnError;
            runner.ProcedureCompleted += OnCompleted;
        }

        private void OnDestroy()
        {
            if (machine == null || machine.Runner == null)
            {
                return;
            }

            var runner = machine.Runner;
            runner.StepCompleted -= OnStepCompleted;
            runner.ViolationRaised -= OnViolation;
            runner.AttemptRefused -= OnRefused;
            runner.StepUndone -= OnUndone;
            runner.StepStarted -= OnStepStarted;
            runner.ErrorRecorded -= OnError;
            runner.ProcedureCompleted -= OnCompleted;
        }

        private string Instruction(StepDefinition step) => step != null ? StepInstructions.Spoken(step, machine.Catalog, InputModalityProbe.Current) : string.Empty;

        private void OnStepCompleted(int index, StepDefinition step, double seconds)
        {
            // StepCompleted fires before the runner advances: the next step is the one after the completed index.
            var steps = machine.Runner.Definition.Steps;
            var next = index + 1 < steps.Count ? steps[index + 1] : null;
            var total = steps.Count;
            loop.RecordEvent($"step {index + 1} '{step.Title}' completed");
            if (next != null)
            {
                loop.Narrate(Narration.StepDone(index + 1, total, next, Instruction(next)), interrupt: false);
            }
        }

        private void OnStepStarted(int index, StepDefinition step)
        {
            if (index == 0)
            {
                safetyLines.Reset(); // a new run
            }
        }

        private void OnViolation(SafetyRule rule, InteractionEvent e)
        {
            loop.RecordEvent($"safety violation: {rule.Description}");
            if (safetyLines.ShouldSpeak(rule, e.Time))
            {
                loop.Narrate(Narration.Violation(rule), interrupt: true);
            }
        }

        // Every later attempt the interlock holds: the violation is only raised once per rule and run.
        private void OnRefused(SafetyRule rule, InteractionEvent e)
        {
            if (safetyLines.ShouldSpeak(rule, e.Time))
            {
                loop.RecordEvent($"held by the interlock: {rule.Description}");
                loop.Narrate(Narration.StillHeld(rule), interrupt: true);
            }
        }

        private void OnUndone(int index, StepDefinition step, InteractionEvent e)
        {
            var part = machine.Catalog?.DisplayName(step.PartId) ?? step.PartId;
            loop.RecordEvent($"step {index + 1} '{step.Title}' undone: {step.PartId} is now {e.Value}");
            loop.Narrate(Narration.Undone(part.ToLowerInvariant(), step.TargetState, Instruction(step)), interrupt: true);
        }

        private void OnError(ProcedureError error)
        {
            var runner = machine.Runner;
            var current = runner.State == RunnerState.Running ? runner.CurrentStep : null;
            loop.RecordEvent($"mistake: {error.Message}");
            loop.Narrate(Narration.Mistake(error, runner.CurrentStepIndex + 1, current, Instruction(current)), interrupt: true);
        }

        private void OnCompleted(ProcedureResult result)
        {
            loop.RecordEvent($"procedure completed, score {result.Score}, {(result.Passed ? "passed" : "not passed")}");
            loop.Narrate(Narration.Completed(result), interrupt: false);
        }
    }
}
