using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Providers;
using Fieldmate.XR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The voice assistant loop (design.md §5.4): push-to-talk → speech-to-text → chat with tool calls executed in the
    /// scene → streamed speech, with the state and transcript on the <see cref="AssistantPanel"/>. Hold X on the left
    /// controller, pinch-and-hold left thumb + middle finger, or hold Space in the Editor. Pressing while the assistant
    /// speaks interrupts it.
    /// </summary>
    public sealed class VoiceLoop : MonoBehaviour, IAssistantScene
    {

        [SerializeField] private MachineServices machine;
        [SerializeField] private AssistantPanel panel;
        [SerializeField] private PartHighlighter highlighter;
        [SerializeField] private StreamingAudioPlayer player;
        [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor leftHand;
        [SerializeField] private Transform head;

        private readonly List<string> notes = new();
        private readonly MicrophoneRecorder recorder = new();
        private InputAction talkAction;
        private AssistantSession session;
        private CancellationTokenSource turn;
        private string idleHint = InputWords.Talk(InputModalityProbe.Current);
        private readonly TalkGate gate = new(); // press/release decisions: flicker grace, interrupt only on speech
        private readonly SpeechDetector speech = new();
        private bool speakingTail; // turn finished, speech still audible

        public AssistantSession Session => session;
        public string DisabledReason { get; private set; }
        public IReadOnlyList<string> Notes => notes;
        double IAssistantScene.Now => machine.Now;

        private void Start()
        {
            InputModalityProbe.Changed += OnModalityChanged;
            panel.SetHead(head);
            talkAction = new InputAction("Talk", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            talkAction.AddBinding("<MetaAimHand>{LeftHand}/middlePressed");
            talkAction.AddBinding("<Keyboard>/space");
            talkAction.started += _ => OnTalkPressed();
            talkAction.canceled += _ => OnTalkReleased();
            talkAction.Enable();

            var providers = AssistantProviders.Load();
            if (providers.IsEnabled)
            {
                Configure(providers.Chat, providers.SpeechToText, providers.TextToSpeech);
                Debug.Log($"[Assistant] providers from {providers.Source}");
            }
            else
            {
                Disable(providers.DisabledReason);
            }
        }

        private void OnModalityChanged(Modality modality)
        {
            if (DisabledReason != null)
            {
                return;
            }

            idleHint = InputWords.Talk(modality);
            if (session != null && session.State == AssistantState.Idle && !speakingTail)
            {
                panel.SetState(AssistantState.Idle, idleHint);
            }
        }

        private void OnDestroy()
        {
            InputModalityProbe.Changed -= OnModalityChanged;
            turn?.Cancel();
            talkAction?.Dispose();
        }

        /// <summary>Builds the session around the given providers (also used by tests with fakes).</summary>
        public void Configure(IChatModel chat, ISpeechToText speechToText, ITextToSpeech textToSpeech)
        {
            var registry = FieldmateTools.CreateRegistry(includeVision: false); // identify_view arrives with M2 vision
            var tools = new AssistantTools(machine.Manual, machine.Runner, machine.Telemetry, this);
            tools.AttachTo(registry);

            session = new AssistantSession(chat, speechToText, textToSpeech, registry, new ConversationState(),
                language => AssistantPrompt.System(machine.Manual.MachineName, language, vision: false), BuildContext, () => Time.realtimeSinceStartupAsDouble);
            tools.LanguageChanged += language => session.Language = language;
            session.StateChanged += ShowState;
            session.TranscriptAdded += panel.Add;
            session.Fallback = new FallbackResponses(machine.Manual, machine.Runner, machine.Telemetry);
            session.OfflineChanged += offline =>
            {
                panel.ShowBanner(offline ? "Offline: scripted answers from the manual until the service is back." : null);
                Debug.Log(offline ? "[Assistant] offline: scripted answers" : "[Assistant] back online");
            };

            DisabledReason = null;
            panel.ShowBanner(null);
            panel.SetState(AssistantState.Idle, idleHint);
        }

        private void Disable(string reason)
        {
            DisabledReason = reason;
            session = null;
            idleHint = "Assistant offline";
            panel.ShowBanner(reason);
            panel.SetState(AssistantState.Idle, idleHint);
            Debug.LogWarning($"[Assistant] {reason}");
        }

        /// <summary>Runs a typed question (debug, tests, the AI eval) through the full loop.</summary>
        public Task<TurnResult> AskAsync(string question, bool speak = true)
        {
            if (session == null)
            {
                throw new InvalidOperationException(DisabledReason ?? "Assistant not configured.");
            }

            CancelTurn();
            turn = new CancellationTokenSource();
            return RunAndLog(session.RunTextTurnAsync(question, speak ? player : null, turn.Token));
        }

        private void OnTalkPressed()
        {
            if (session == null)
            {
                return;
            }

            // Grabbing a control with the left hand also closes the middle finger: not a talk press (device 2026-09-30).
            if (leftHand != null && leftHand.hasSelection)
            {
                return;
            }

            var busy = session.State != AssistantState.Idle || player.IsPlaying;
            switch (gate.Press(Time.unscaledTime, busy))
            {
                case TalkAction.Listen:
                    speech.Reset();
                    if (!session.BeginListening() || !StartMicrophone())
                    {
                        session.CancelListening();
                        gate.Reset();
                    }

                    break;
                case TalkAction.StartPending:
                    // Pressing isn't speaking: open the microphone, lower the reply, and interrupt only once speech is heard.
                    speech.Reset();
                    if (StartMicrophone())
                    {
                        player.Duck(true);
                    }
                    else
                    {
                        gate.Reset();
                    }

                    break;
            }
        }

        private void OnTalkReleased()
        {
            switch (gate.Release(Time.unscaledTime))
            {
                case TalkAction.DropStray:
                    recorder.Stop();
                    player.Duck(false);
                    Debug.Log($"[Assistant] press without speech ignored while {session?.State} (peak level {speech.Peak:0.000})");
                    break;
                case TalkAction.CheckSpeech:
                    var audio = recorder.Stop();
                    player.Duck(false);
                    if (speech.Peak < SpeechDetector.SilencePeak)
                    {
                        Debug.Log($"[Assistant] press without speech ignored while {session?.State} ({audio?.DurationSeconds:0.00}s of silence)");
                        break;
                    }

                    Debug.Log($"[Assistant] press without detected speech ({audio?.DurationSeconds:0.00}s, peak level {speech.Peak:0.000}): checking with speech-to-text");
                    _ = InterruptIfSpokenAsync(audio);
                    break;
            }
        }

        private void Update()
        {
            if (speakingTail && !player.IsPlaying)
            {
                speakingTail = false;
                panel.SetState(session != null ? session.State : AssistantState.Idle, idleHint);
            }

            if (pendingNarration != null && session != null && session.State == AssistantState.Idle && !player.IsPlaying && !gate.IsRecording)
            {
                var text = pendingNarration;
                pendingNarration = null;
                Narrate(text, interrupt: false);
            }

            if (gate.IsRecording && recorder.IsRecording &&
                speech.Add(recorder.Level(), Time.unscaledDeltaTime) &&
                gate.SpeechDetected() == TalkAction.Interrupt)
            {
                Interrupt();
            }

            if (gate.Tick(Time.unscaledTime) == TalkAction.Finish)
            {
                FinishRecording();
            }
        }

        private bool StartMicrophone()
        {
            if (recorder.Start())
            {
                return true;
            }

            Debug.LogWarning("[Assistant] microphone failed to start");
            panel.Add(new TranscriptEntry(TranscriptKind.Error, "No microphone available (or permission denied)."));
            return false;
        }

        private void Interrupt()
        {
            Debug.Log($"[Assistant] speech while {session.State} (playing={player.IsPlaying}, level {speech.Peak:0.000}): interrupting");
            CancelTurn(); // barge-in: stop thinking or speaking
            if (!session.BeginListening(interrupt: true))
            {
                recorder.Stop();
                gate.Reset();
                Debug.LogWarning($"[Assistant] interrupt refused in state {session.State}");
            }
        }

        // A long press during a reply whose speech the level check missed (quiet voice): interrupt only if words come back.
        private async Task InterruptIfSpokenAsync(AudioData audio)
        {
            var text = await session.TranscribeOnlyAsync(audio, CancellationToken.None);
            if (string.IsNullOrEmpty(text))
            {
                Debug.Log("[Assistant] no speech in that press; the reply continues");
                return;
            }

            if (gate.IsRecording)
            {
                return; // the user has started another press meanwhile; that one wins
            }

            Debug.Log($"[Assistant] speech-to-text heard \"{Clip(text)}\": interrupting");
            CancelTurn();
            turn = new CancellationTokenSource();
            _ = RunAndLog(session.RunTextTurnAsync(text, player, turn.Token));
        }

        private void FinishRecording()
        {
            if (session == null || session.State != AssistantState.Listening)
            {
                if (recorder.IsRecording)
                {
                    recorder.Stop(); // never leave the microphone open, or the next press can't start it
                    Debug.LogWarning($"[Assistant] recording dropped: state changed to {session?.State} while listening");
                }

                return;
            }

            var audio = recorder.Stop();
            if (audio == null || audio.DurationSeconds < TalkGate.MinSpeechSeconds)
            {
                session.CancelListening();
                Debug.Log($"[Assistant] recording dropped: {(audio == null ? "no audio" : $"{audio.DurationSeconds:0.00}s")} is too short (peak level {speech.Peak:0.000})");
                return;
            }

            if (speech.Peak < SpeechDetector.SilencePeak)
            {
                session.CancelListening();
                Debug.Log($"[Assistant] recording dropped: {audio.DurationSeconds:0.00}s of silence (peak level {speech.Peak:0.000})");
                return;
            }

            Debug.Log($"[Assistant] recorded {audio.DurationSeconds:0.00}s (peak level {speech.Peak:0.000})");

            turn = new CancellationTokenSource();
            _ = RunAndLog(session.RunAudioTurnAsync(audio, player, turn.Token));
        }

        private async Task<TurnResult> RunAndLog(Task<TurnResult> running)
        {
            if (machine.Runner.State == RunnerState.Running)
            {
                machine.Runner.Handle(InteractionEvent.Help(machine.Now)); // debrief: assistant reliance
            }

            try
            {
                var result = await running;
                Debug.Log($"[Assistant] turn ok={result.Success} tools=[{string.Join(",", result.ToolsUsed)}] {result.Timings}" +
                          $" | user: \"{Clip(result.UserText)}\" | answer: \"{Clip(result.AssistantText)}\"");
                return result;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                panel.Add(new TranscriptEntry(TranscriptKind.Error, "Something went wrong with the assistant."));
                return null;
            }
        }

        private static string Clip(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : text.Length <= 140 ? text : text.Substring(0, 140) + "…";

        private void CancelTurn()
        {
            turn?.Cancel();
            turn = null;
            if (player != null)
            {
                player.Stop();
            }
        }

        private string pendingNarration;

        /// <summary>Records a machine event for the model's context (#61).</summary>
        public void RecordEvent(string text) => session?.SceneEvents.Add(Time.realtimeSinceStartupAsDouble, text);

        /// <summary>
        /// Speaks a line without being asked (#61). <paramref name="interrupt"/> stops whatever is playing first (violations,
        /// wrong order); otherwise a line waits until the assistant is idle so it never talks over an answer.
        /// </summary>
        public void Narrate(string text, bool interrupt)
        {
            if (session == null)
            {
                panel.Add(new TranscriptEntry(TranscriptKind.Assistant, text));
                return;
            }

            if (player == null)
            {
                return; // scene torn down
            }

            var busy = session.State != AssistantState.Idle || player.IsPlaying || gate.IsRecording;
            if (busy && !interrupt)
            {
                pendingNarration = text; // the newest wins; the step card shows the rest
                return;
            }

            if (interrupt)
            {
                CancelTurn();
                gate.Reset();
                recorder.Stop();
            }

            pendingNarration = null;
            turn = new CancellationTokenSource();
            Debug.Log($"[Assistant] narrate{(interrupt ? " (interrupt)" : "")}: \"{Clip(text)}\"");
            _ = RunAndLog(NarrateAsync(text, turn.Token));
        }

        private async Task<TurnResult> NarrateAsync(string text, CancellationToken token)
        {
            await session.SpeakAsync(text, player, token);
            return session.LastTurn;
        }

        // The session is idle once the last audio has arrived; the panel keeps "Speaking…" until it has been heard.
        private void ShowState(AssistantState state)
        {
            if (player == null || panel == null)
            {
                return; // a narration finishing after the scene was torn down (tests)
            }

            speakingTail = state == AssistantState.Idle && player.IsPlaying;
            panel.SetState(speakingTail ? AssistantState.Speaking : state, idleHint);
        }

        private string BuildContext(string userText)
        {
            var gaze = head != null ? machine.PartAlong(new Ray(head.position, head.forward)) : null;
            var step = machine.Runner.State == RunnerState.Running ? machine.Runner.CurrentStep.Id : null;
            var slice = machine.Retriever.Retrieve(new RetrievalQuery(gaze?.Id, step, userText));
            var events = session != null ? session.SceneEvents.ToPromptText(Time.realtimeSinceStartupAsDouble) : null;
            return AssistantPrompt.Context(machine.Runner, machine.Telemetry, gaze, slice, events);
        }

        // ---------- IAssistantScene ----------

        bool IAssistantScene.TryHighlightPart(string partId)
        {
            if (!machine.TryGetPart(partId, out var part))
            {
                return false;
            }

            highlighter.Highlight(part, machine.Manual.TryGetPart(partId, out var info) ? info.Name : partId);
            return true;
        }

        void IAssistantScene.ShowManualSection(ManualSection section) => panel.ShowSection(section);

        void IAssistantScene.ShowStep(ProcedureDefinition procedure, int stepNumber) => panel.ShowStep(procedure, stepNumber);

        void IAssistantScene.AddNote(string text)
        {
            notes.Add(text);
            panel.ShowNotes(notes);
        }

        string IAssistantScene.IdentifyView() => null; // M2 Vision
    }
}
