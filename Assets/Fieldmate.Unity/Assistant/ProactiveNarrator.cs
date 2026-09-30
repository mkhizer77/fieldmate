using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.XR;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Makes the assistant speak on its own (#61): a completed step announces the next one, a safety violation or an
    /// out-of-order action interrupts whatever is playing, the debrief reads the score. Every event also goes into the
    /// session's scene log so the model knows what just happened when the user asks.
    /// </summary>
    public sealed class ProactiveNarrator : MonoBehaviour
    {
        [SerializeField] private MachineServices machine;
        [SerializeField] private VoiceLoop loop;

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
            runner.ErrorRecorded -= OnError;
            runner.ProcedureCompleted -= OnCompleted;
        }

        private string Instruction(StepDefinition step) => step != null ? StepInstructions.For(step, machine.Catalog, InputModalityProbe.Current) : string.Empty;

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

        private void OnViolation(SafetyRule rule, InteractionEvent e)
        {
            loop.RecordEvent($"safety violation: {rule.Description}");
            loop.Narrate(Narration.Violation(rule), interrupt: true);
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
