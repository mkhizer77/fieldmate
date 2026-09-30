using Fieldmate.Assistant;
using Fieldmate.Interaction;
using Fieldmate.Twin;
using Fieldmate.UI;
using Fieldmate.XR;
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
        [SerializeField] private MachinePlacement placement;
        [SerializeField] private ControlGuide guide;

        private const float RestartConfirmSeconds = 3f;

        private readonly DwellTracker dwell = new();
        private readonly System.Collections.Generic.HashSet<string> shownTips = new();
        private RotaryInteractable watched;
        private GazeRing gazeRing;
        private Vector3 gazePoint;
        private float restartArmedUntil = -1f;
        private bool dwellReported;
        private int readsTaken;
        private bool readingShown;
        private bool runsBefore;
        private bool placementConfirmed;

        /// <summary>Placement is step 0: the procedure can't start until the machine stands where the user wants it.</summary>
        public bool ReadyToStart => placementConfirmed || placement == null;

        public void Configure(MachineServices services, MachineControlRouter router, PartHighlighter partHighlighter,
            ProcedurePanel procedurePanel, PressButton startButton, Transform headTransform,
            MachinePlacement machinePlacement = null, ControlGuide stepGuide = null)
        {
            placement = machinePlacement;
            guide = stepGuide;
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
            runner.AttemptRefused += OnAttemptRefused;
            runner.ErrorRecorded += OnError;
            runner.ProcedureCompleted += OnCompleted;
            button.Pressed += OnButton;
            InputModalityProbe.Changed += OnModalityChanged;
            if (placement != null)
            {
                placement.StateChanged += OnPlacementChanged;
            }

            panel.SetHead(head);
            gazeRing = GazeRing.Create(head);
            ShowIdle();
        }

        // Ring over the step's part while the gaze rests on it: fills over the dwell the step needs.
        private void UpdateGazeRing(StepDefinition step, float seconds)
        {
            if (gazeRing == null)
            {
                return;
            }

            var needed = step.Kind == StepKind.Inspect ? step.DwellSeconds : step.Kind == StepKind.Measure ? GaugeCheck.ReadSeconds : 0f;
            if (needed <= 0f || dwell.PartId == null || dwell.PartId != step.PartId)
            {
                gazeRing.Hide();
                return;
            }

            // At the point the gaze meets the part (smoothed), a little toward the viewer: right where the user is looking.
            var cycle = step.Kind == StepKind.Measure ? seconds % needed : seconds; // a reading repeats every ReadSeconds
            var toViewer = (head.position - gazePoint).normalized;
            gazeRing.Show(gazePoint + toViewer * 0.03f, cycle / needed);
        }

        // The step's two-hand control, watched while its step runs (controls register in their own Start, so this is
        // done per step rather than once at startup).
        private void WatchControl(StepDefinition step)
        {
            if (watched != null)
            {
                watched.HoldingChanged -= OnHoldingChanged;
                watched = null;
            }

            if (step?.Kind != StepKind.Operate || controls == null)
            {
                return;
            }

            foreach (var control in controls.Controls)
            {
                if (control.PartId == step.PartId && control is RotaryInteractable rotary && rotary.RequiredHands > 1)
                {
                    watched = rotary;
                    watched.HoldingChanged += OnHoldingChanged;
                }
            }
        }

        // One hand on a two-hand control during its step: say what the other hand has to do (device test 2026-09-30).
        private void OnHoldingChanged(RotaryInteractable control, int hands)
        {
            var runner = machine.Runner;
            if (runner.State != RunnerState.Running || runner.CurrentStep?.PartId != control.PartId)
            {
                return;
            }

            if (hands == 1)
            {
                panel.ShowStatus(InteractionTips.SecondHand(InputModalityProbe.Current), Theme.Accent, 8f);
            }
            else if (hands == 2)
            {
                panel.ShowStatus("Both hands on. Turn them together.", Theme.Accent, 3f);
            }
        }

        private void OnPlacementChanged(PlacementState state)
        {
            if (state == PlacementState.Placed && !placement.Restored)
            {
                placementConfirmed = true; // placed by the user this session
            }

            if (machine.Runner.State != RunnerState.Running)
            {
                ShowIdle();
            }
        }

        private void OnModalityChanged(Modality modality)
        {
            var runner = machine.Runner;
            if (runner.State == RunnerState.Running)
            {
                OnStepStarted(runner.CurrentStepIndex, runner.CurrentStep, fresh: false);
            }
            else
            {
                ShowIdle();
            }
        }

        // Before a run: step 0 (placement) until the machine is placed, then the Start prompt.
        private void ShowIdle()
        {
            var modality = InputModalityProbe.Current;
            var total = machine.Runner.Definition.Steps.Count;
            if (ReadyToStart)
            {
                panel.ShowIdle(machine.Runner.Definition.Title,
                    $"{total} steps. {InputWords.Press(modality)} Start, or ask Fieldmate to start the procedure.", "Ready", "Procedure");
                button.SetLabel("Start");
                button.SetStyle(ButtonStyle.Primary);
                return;
            }

            switch (placement.State)
            {
                case PlacementState.Placed:
                    panel.ShowIdle("Step 0: Place the machine",
                        $"It is where you left it. {InputWords.Press(modality)} Keep here to continue, or Move machine to place it again.",
                        "Setup", "Before you start");
                    button.SetLabel("Keep here");
                    button.SetStyle(ButtonStyle.Primary);
                    break;
                case PlacementState.Placing:
                    panel.ShowIdle("Step 0: Place the machine", InputWords.Place(modality) + ". Leave room to walk around it.", "Setup", "Before you start");
                    button.SetLabel("Place first");
                    button.SetStyle(ButtonStyle.Muted);
                    break;
                default:
                    panel.ShowIdle("Step 0: Place the machine", "Looking for where you left it…", "Setup", "Before you start");
                    button.SetLabel("Place first");
                    button.SetStyle(ButtonStyle.Muted);
                    break;
            }
        }

        private void OnDestroy()
        {
            InputModalityProbe.Changed -= OnModalityChanged; // static event: always unsubscribe, even if services are gone
            if (watched != null)
            {
                watched.HoldingChanged -= OnHoldingChanged;
            }

            if (placement != null)
            {
                placement.StateChanged -= OnPlacementChanged;
            }

            if (machine == null || machine.Runner == null)
            {
                return;
            }

            var runner = machine.Runner;
            runner.StepStarted -= OnStepStarted;
            runner.StepCompleted -= OnStepCompleted;
            runner.ViolationRaised -= OnViolation;
            runner.AttemptRefused -= OnAttemptRefused;
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
                if (!ReadyToStart)
                {
                    if (placement.State == PlacementState.Placed)
                    {
                        placementConfirmed = true; // "Keep here"
                        Debug.Log("[Procedure] placement kept");
                        ShowIdle();
                    }

                    return;
                }

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
            var gazed = machine.PartAlong(new Ray(head.position, head.forward), out var gazeHit, GazeRange);
            gazePoint = gazed != null ? (gazePoint == Vector3.zero ? gazeHit : Vector3.Lerp(gazePoint, gazeHit, 0.35f)) : Vector3.zero;
            var previous = dwell.PartId;
            var seconds = dwell.Update(gazed?.Id, Time.deltaTime);
            if (dwell.PartId != previous)
            {
                Debug.Log($"[Procedure] gaze {dwell.PartId ?? "none"}"); // only on change
            }

            UpdateGazeRing(step, seconds);

            if (dwell.PartId == null || dwell.PartId != step.PartId)
            {
                dwellReported = false;
                readsTaken = 0;
                readingShown = false;
                KeepHighlighted(step);
                return;
            }

            if (step.Kind == StepKind.Inspect && !dwellReported && seconds >= step.DwellSeconds)
            {
                dwellReported = true;
                runner.Handle(InteractionEvent.Gaze(machine.Now, step.PartId, seconds));
            }
            else if (step.Kind == StepKind.Measure && readsTaken == 0 && !readingShown)
            {
                readingShown = true; // the user sees the look is registering before the first reading lands
                panel.ShowStatus("Reading the gauge…", Theme.Accent, GaugeCheck.ReadSeconds + 0.5f);
            }

            if (step.Kind == StepKind.Measure && seconds >= (readsTaken + 1) * GaugeCheck.ReadSeconds)
            {
                // A fresh reading every ReadSeconds while the gaze stays on the gauge: watching the needle fall to zero
                // completes the step by itself (device test 2026-09-30: one reading per look left the user stuck).
                readsTaken++;
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
                var label = $"{machine.Catalog.DisplayName(step.PartId)}\n<size=26>{StepInstructions.Short(step, InputModalityProbe.Current)}</size>";
                highlighter.Highlight(part, label, float.PositiveInfinity);
            }
        }

        private void OnStepStarted(int index, StepDefinition step) => OnStepStarted(index, step, fresh: true);

        private void OnStepStarted(int index, StepDefinition step, bool fresh)
        {
            placementConfirmed = true; // a run started (e.g. by voice) means the machine is where the user wants it
            if (!fresh)
            {
                panel.ShowStep(index + 1, machine.Runner.Definition.Steps.Count, step.Title,
                    StepInstructions.For(step, machine.Catalog, InputModalityProbe.Current), machine.Runner.Definition.Title);
                return;
            }

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
            readsTaken = 0;
            readingShown = false;
            highlighter.Clear();
            panel.ShowStep(index + 1, machine.Runner.Definition.Steps.Count, step.Title,
                StepInstructions.For(step, machine.Catalog, InputModalityProbe.Current), machine.Runner.Definition.Title);
            ShowGuide(step);
            WatchControl(step);
            ShowTip(step);
            button.SetLabel("Restart");
            button.SetStyle(ButtonStyle.Secondary);
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

        // Every try on a held part says why it didn't move (the violation itself is spoken once).
        private void OnAttemptRefused(SafetyRule rule, InteractionEvent e) =>
            panel.ShowStatus($"Safety: {rule.Description}", ProcedurePanel.Violation, 8f);

        private void OnError(ProcedureError error)
        {
            panel.ShowStatus(error.Message, ProcedurePanel.Hint, 6f);
            Debug.Log($"[Procedure] error {error}");
        }

        // Once per kind of interaction per session: how to hold and move the hand for this kind of step.
        private void ShowTip(StepDefinition step)
        {
            var hands = 1;
            if (step.Kind == StepKind.Operate && controls != null)
            {
                foreach (var control in controls.Controls)
                {
                    if (control.PartId == step.PartId && control is RotaryInteractable rotary)
                    {
                        hands = rotary.RequiredHands;
                    }
                }
            }

            var key = InteractionTips.Key(step, hands);
            if (key == null || !shownTips.Add(key))
            {
                return;
            }

            panel.ShowStatus(InteractionTips.For(step, hands, InputModalityProbe.Current), Theme.Accent, InteractionTips.Seconds);
        }

        // The way to operate the step's control: arc to the target position, arrow off the machine, or a line to the socket.
        private void ShowGuide(StepDefinition step)
        {
            if (guide == null)
            {
                return;
            }

            guide.Hide();
            if (step.Kind == StepKind.Tool)
            {
                ToolItem tool = null;
                foreach (var item in FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
                {
                    if (item.ToolId == step.ToolId) tool = item;
                }

                foreach (var socket in controls.Sockets)
                {
                    if (socket.SocketId == step.PartId && tool != null)
                    {
                        guide.ShowCarry(tool.transform, socket.transform);
                    }
                }

                return;
            }

            if (step.Kind != StepKind.Operate)
            {
                return;
            }

            foreach (var control in controls.Controls)
            {
                if (control.PartId != step.PartId)
                {
                    continue;
                }

                if (control is RotaryInteractable rotary && rotary.TryGetDetentAngle(step.TargetState, out var target))
                {
                    guide.ShowRotary(rotary, target, rotary.RequiredHands > 1 ? 0.2f : 0.1f);
                }
                else if (control is RemovablePart cover && step.TargetState == RemovablePart.Removed)
                {
                    guide.ShowPull(cover.transform, cover.transform.parent != null ? cover.transform.parent.forward : cover.transform.forward);
                }
            }
        }

        private void OnCompleted(ProcedureResult result)
        {
            guide?.Hide();
            gazeRing?.Hide();
            WatchControl(null);
            highlighter.Clear();
            panel.ShowDebrief(result, machine.Runner.Definition.Title);
            button.SetLabel("Run again");
            button.SetStyle(ButtonStyle.Primary);
            Debug.Log($"[Procedure] completed: score {result.Score}, passed {result.Passed}, {result.TotalSeconds:0}s, " +
                      $"{result.Errors.Count} errors, {result.Violations.Count} violations, {result.HelpRequests} help");
        }
    }
}
