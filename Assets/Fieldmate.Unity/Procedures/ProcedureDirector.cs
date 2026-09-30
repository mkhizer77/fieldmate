using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Twin;
using UnityEngine;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// Runs the procedure in the scene (design.md §5.2 ProcedureRunnerBehaviour + StepPresenter): the Start button or the
    /// assistant's start_procedure tool begins it; the step's part stays highlighted with the floating marker; gaze dwell
    /// completes Inspect steps and reads the gauge for Measure steps (only in-tolerance readings are reported, so an early
    /// look is a hint, not an error); violations and errors show on the panel; the debrief follows completion.
    /// Machine controls report their own events through <see cref="MachineControlRouter"/>. No per-frame allocations.
    /// </summary>
    public sealed class ProcedureDirector : MonoBehaviour
    {
        private const float GazeRange = 4f;

        [SerializeField] private MachineServices machine;
        [SerializeField] private MachineControlRouter controls;
        [SerializeField] private PartHighlighter highlighter;
        [SerializeField] private ProcedurePanel panel;
        [SerializeField] private PressButton button;
        [SerializeField] private Transform head;

        private const float RestartConfirmSeconds = 3f;

        private readonly DwellTracker dwell = new();
        private float restartArmedUntil = -1f;
        private bool dwellReported;
        private bool runsBefore;

        public void Configure(MachineServices services, MachineControlRouter router, PartHighlighter partHighlighter,
            ProcedurePanel procedurePanel, PressButton startButton, Transform headTransform)
        {
            machine = services;
            controls = router;
            highlighter = partHighlighter;
            panel = procedurePanel;
            button = startButton;
            head = headTransform;
        }

        private void Start()
        {
            var runner = machine.Runner;
            runner.StepStarted += OnStepStarted;
            runner.StepCompleted += OnStepCompleted;
            runner.ViolationRaised += OnViolation;
            runner.ErrorRecorded += OnError;
            runner.ProcedureCompleted += OnCompleted;
            button.Pressed += OnButton;
            panel.SetHead(head);
            panel.ShowIdle(runner.Definition.Title, "Press Start, or ask Fieldmate to start the procedure.");
            button.SetLabel("Start");
        }

        private void OnDestroy()
        {
            if (machine == null || machine.Runner == null)
            {
                return;
            }

            var runner = machine.Runner;
            runner.StepStarted -= OnStepStarted;
            runner.StepCompleted -= OnStepCompleted;
            runner.ViolationRaised -= OnViolation;
            runner.ErrorRecorded -= OnError;
            runner.ProcedureCompleted -= OnCompleted;
            if (button != null)
            {
                button.Pressed -= OnButton;
            }
        }

        /// <summary>Starts or restarts the procedure (the button; the assistant's tool restarts the runner directly).</summary>
        public void StartProcedure() => machine.Runner.Restart(machine.Now);

        // During a run, one press only arms a restart and a second press confirms it: stray pinches near the button
        // restarted the procedure a dozen times in the first device test.
        private void OnButton()
        {
            if (machine.Runner.State != RunnerState.Running)
            {
                StartProcedure();
                return;
            }

            if (Time.unscaledTime <= restartArmedUntil)
            {
                restartArmedUntil = -1f;
                StartProcedure();
                return;
            }

            restartArmedUntil = Time.unscaledTime + RestartConfirmSeconds;
            button.SetLabel("Confirm restart");
            panel.ShowStatus("Press again to restart the procedure from step 1.", ProcedurePanel.Hint, RestartConfirmSeconds);
        }

        private void Update()
        {
            if (restartArmedUntil > 0f && Time.unscaledTime > restartArmedUntil)
            {
                restartArmedUntil = -1f;
                button.SetLabel("Restart");
            }

            var runner = machine.Runner;
            if (runner.State != RunnerState.Running || head == null)
            {
                return;
            }

            var step = runner.CurrentStep;
            var gazed = machine.PartAlong(new Ray(head.position, head.forward), GazeRange);
            var previous = dwell.PartId;
            var seconds = dwell.Update(gazed?.Id, Time.deltaTime);
            if (dwell.PartId != previous)
            {
                Debug.Log($"[Procedure] gaze {dwell.PartId ?? "none"}"); // only on change
            }

            if (dwell.PartId == null || dwell.PartId != step.PartId)
            {
                dwellReported = false;
                KeepHighlighted(step);
                return;
            }

            if (step.Kind == StepKind.Inspect && !dwellReported && seconds >= step.DwellSeconds)
            {
                dwellReported = true;
                runner.Handle(InteractionEvent.Gaze(machine.Now, step.PartId, seconds));
            }
            else if (step.Kind == StepKind.Measure && !dwellReported && seconds >= GaugeCheck.ReadSeconds)
            {
                dwellReported = true; // one reading per look: look away and back to read again
                var reading = machine.Telemetry[TelemetryChannel.Pressure];
                if (GaugeCheck.TryRead(step, reading, out var hint))
                {
                    runner.Handle(InteractionEvent.Measured(machine.Now, reading));
                }
                else
                {
                    panel.ShowStatus(hint, ProcedurePanel.Hint, 6f);
                }
            }

            KeepHighlighted(step);
        }

        // The step's part keeps its marker; an assistant highlight may take over briefly, then the step's returns.
        private void KeepHighlighted(StepDefinition step)
        {
            if (step?.PartId != null && highlighter.ActivePartId == null && machine.TryGetPart(step.PartId, out var part))
            {
                highlighter.Highlight(part, machine.Catalog.DisplayName(step.PartId), float.PositiveInfinity);
            }
        }

        private void OnStepStarted(int index, StepDefinition step)
        {
            if (index == 0)
            {
                // A repeat run, however it was started, begins from the faulty machine again.
                if (runsBefore && controls != null)
                {
                    controls.ResetForNewRun();
                }

                runsBefore = true;
            }

            dwell.Reset();
            dwellReported = false;
            highlighter.Clear();
            panel.ShowStep(index + 1, machine.Runner.Definition.Steps.Count, step.Title, StepInstructions.For(step, machine.Catalog));
            button.SetLabel("Restart");
            KeepHighlighted(step);
            Debug.Log($"[Procedure] step {index + 1}: {step.Id}");
        }

        private void OnStepCompleted(int index, StepDefinition step, double seconds)
        {
            panel.ShowStatus($"✓ {step.Title}", ProcedurePanel.Done, 3f);
            Debug.Log($"[Procedure] done {step.Id} in {seconds:0.0}s");
        }

        private void OnViolation(SafetyRule rule, InteractionEvent e)
        {
            panel.ShowStatus($"Safety: {rule.Description}", ProcedurePanel.Violation, 8f);
            Debug.Log($"[Procedure] violation {rule.Id} by {e}");
        }

        private void OnError(ProcedureError error)
        {
            panel.ShowStatus(error.Message, ProcedurePanel.Hint, 6f);
            Debug.Log($"[Procedure] error {error}");
        }

        private void OnCompleted(ProcedureResult result)
        {
            highlighter.Clear();
            panel.ShowDebrief(result, machine.Runner.Definition.Title);
            button.SetLabel("Run again");
            Debug.Log($"[Procedure] completed: score {result.Score}, passed {result.Passed}, {result.TotalSeconds:0}s, " +
                      $"{result.Errors.Count} errors, {result.Violations.Count} violations, {result.HelpRequests} help");
        }
    }
}
