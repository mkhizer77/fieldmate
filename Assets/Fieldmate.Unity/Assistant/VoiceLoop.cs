using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Providers;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

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
        private const float MinRecordingSeconds = 0.35f;

        [SerializeField] private MachineServices machine;
        [SerializeField] private AssistantPanel panel;
        [SerializeField] private PartHighlighter highlighter;
        [SerializeField] private StreamingAudioPlayer player;
        [SerializeField] private Transform head;

        private readonly List<string> notes = new();
        private readonly MicrophoneRecorder recorder = new();
        private InputAction talkAction;
        private AssistantSession session;
        private CancellationTokenSource turn;
        private string idleHint = "Hold X or pinch (left middle finger) to talk";
        private readonly TalkGate gate = new(); // press/release timing: flicker grace and hold-to-interrupt
        private bool speakingTail; // turn finished, speech still audible

        public AssistantSession Session => session;
        public string DisabledReason { get; private set; }
        public IReadOnlyList<string> Notes => notes;
        double IAssistantScene.Now => machine.Now;

        private void Start()
        {
            panel.SetHead(head);
            talkAction = new InputAction("Talk", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            talkAction.AddBinding("<MetaAimHand>{LeftHand}/middlePressed");
            talkAction.AddBinding("<Keyboard>/space");
            talkAction.started += _ => OnTalkPressed();
            talkAction.canceled += _ => OnTalkReleased();
            talkAction.Enable();

            RequestMicrophone();

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

        private void OnDestroy()
        {
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

            var busy = session.State != AssistantState.Idle || player.IsPlaying;
            switch (gate.Press(Time.unscaledTime, busy))
            {
                case TalkAction.Listen:
                    if (!session.BeginListening() || !StartMicrophone())
                    {
                        session.CancelListening();
                        gate.Reset();
                    }

                    break;
                case TalkAction.StartPending:
                    // Record straight away so a real interrupt keeps its first words; interrupt only once held.
                    if (!StartMicrophone())
                    {
                        gate.Reset();
                    }

                    break;
            }
        }

        private void OnTalkReleased()
        {
            if (gate.Release(Time.unscaledTime) == TalkAction.DropStray)
            {
                recorder.Stop();
                Debug.Log($"[Assistant] ignored short press while {session?.State}");
            }
        }

        private void Update()
        {
            if (speakingTail && !player.IsPlaying)
            {
                speakingTail = false;
                panel.SetState(session != null ? session.State : AssistantState.Idle, idleHint);
            }

            switch (gate.Tick(Time.unscaledTime))
            {
                case TalkAction.Interrupt:
                    Interrupt();
                    break;
                case TalkAction.Finish:
                    FinishRecording();
                    break;
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
            Debug.Log($"[Assistant] held while {session.State} (playing={player.IsPlaying}): interrupting");
            CancelTurn(); // barge-in: stop thinking or speaking
            if (!session.BeginListening(interrupt: true))
            {
                recorder.Stop();
                gate.Reset();
                Debug.LogWarning($"[Assistant] interrupt refused in state {session.State}");
            }
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
            if (audio == null || audio.DurationSeconds < MinRecordingSeconds)
            {
                session.CancelListening();
                Debug.Log($"[Assistant] recording discarded: {(audio == null ? "no audio" : $"{audio.DurationSeconds:0.00}s")} is too short");
                panel.Add(new TranscriptEntry(TranscriptKind.Info, "Too short. Hold while you speak."));
                return;
            }

            Debug.Log($"[Assistant] recorded {audio.DurationSeconds:0.00}s");

            turn = new CancellationTokenSource();
            _ = RunAndLog(session.RunAudioTurnAsync(audio, player, turn.Token));
        }

        private async Task<TurnResult> RunAndLog(Task<TurnResult> running)
        {
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
            player.Stop();
        }

        // The session is idle once the last audio has arrived; the panel keeps "Speaking…" until it has been heard.
        private void ShowState(AssistantState state)
        {
            speakingTail = state == AssistantState.Idle && player.IsPlaying;
            panel.SetState(speakingTail ? AssistantState.Speaking : state, idleHint);
        }

        private string BuildContext(string userText)
        {
            var gaze = head != null ? machine.PartAlong(new Ray(head.position, head.forward)) : null;
            var step = machine.Runner.State == RunnerState.Running ? machine.Runner.CurrentStep.Id : null;
            var slice = machine.Retriever.Retrieve(new RetrievalQuery(gaze?.Id, step, userText));
            return AssistantPrompt.Context(machine.Runner, machine.Telemetry, gaze, slice);
        }

        private static void RequestMicrophone()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
    #endif
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
