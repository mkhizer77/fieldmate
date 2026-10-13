using System;
using System.Threading;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Localization;
using Fieldmate.Procedures;
using UnityEngine;

namespace Fieldmate.Net
{
    /// <summary>
    /// The headset's side of the remote observer (#26): the hand menu's Observer button starts sharing and shows the join
    /// code; the session then streams the twin, the procedure and the transcript to the observer and carries out what it
    /// asks: faults go into the twin, hints highlight the step's part and are spoken, messages are spoken. The headset
    /// stays authoritative and never waits on the observer.
    /// </summary>
    public sealed class TraineeObserverHost : MonoBehaviour
    {
        [SerializeField] private ObserverNetwork network;
        [SerializeField] private MachineServices machine;
        [SerializeField] private VoiceLoop voice;
        [SerializeField] private PartHighlighter highlighter;
        [SerializeField] private AssistantPanel panel;
        [SerializeField] private PressButton button;
        [SerializeField] private bool preferRelay = true;

        private ObserverServer server;
        private CancellationTokenSource starting;

        public ObserverServer Server => server;
        public bool IsSharing => server != null;
        public string JoinCode => network != null ? network.JoinCode : string.Empty;

        public bool PreferRelay
        {
            get => preferRelay;
            set => preferRelay = value;
        }

        public void Configure(ObserverNetwork observerNetwork, MachineServices services, VoiceLoop loop, PartHighlighter parts,
            AssistantPanel assistantPanel, PressButton toggle)
        {
            network = observerNetwork;
            machine = services;
            voice = loop;
            highlighter = parts;
            panel = assistantPanel;
            button = toggle;
        }

        private void Start()
        {
            if (button != null)
            {
                button.Pressed += Toggle;
                button.SetLabel(Lang.T("Observer: Off"));
            }

            if (voice != null)
            {
                voice.TranscriptAdded += OnTranscript;
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.Pressed -= Toggle;
            }

            if (voice != null)
            {
                voice.TranscriptAdded -= OnTranscript;
            }

            starting?.Cancel();
            Stop();
        }

        private void Update() => server?.Tick(Time.unscaledTimeAsDouble);

        public async void Toggle()
        {
            if (IsSharing || starting != null)
            {
                Stop();
                return;
            }

            try
            {
                await StartSharingAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Observer] couldn't share: {e.Message}");
                panel?.ShowBanner(Lang.T("Observer link failed. Check the network and try again."));
                button?.SetLabel(Lang.T("Observer: Off"));
            }
        }

        /// <summary>Starts sharing; returns the code an observer types to join.</summary>
        public async System.Threading.Tasks.Task<string> StartSharingAsync()
        {
            starting = new CancellationTokenSource();
            button?.SetLabel(Lang.T("Observer: starting…"));
            try
            {
                var code = await network.StartServerAsync(preferRelay, starting.Token);
                server = new ObserverServer(network.Link, machine.Telemetry, () => machine.Runner, machine.Manual.MachineName);
                server.CommandApplied += OnCommand;
                server.Warning += w => Debug.LogWarning($"[Observer] {w}");
                button?.SetLabel(Lang.F("Observer: {0}", code));
                var note = string.IsNullOrEmpty(network.RelayNote) ? string.Empty : $" ({Lang.T(network.RelayNote)})";
                panel?.ShowBanner(Lang.F("Observer code: {0}", code) + note);
                return code;
            }
            finally
            {
                starting?.Dispose();
                starting = null;
            }
        }

        public void Stop()
        {
            if (server != null)
            {
                server.CommandApplied -= OnCommand;
                server.Dispose();
                server = null;
                panel?.ShowBanner(null);
                Debug.Log("[Observer] stopped sharing");
            }

            starting?.Cancel();
            network?.Shutdown();
            button?.SetLabel(Lang.T("Observer: Off"));
        }

        private void OnTranscript(TranscriptEntry entry) => server?.AddTranscript(entry);

        private void OnCommand(ObserverCommand command)
        {
            switch (command.Kind)
            {
                case ObserverCommandKind.InjectFault:
                case ObserverCommandKind.ClearFault:
                    // The twin already changed; tell the assistant so "why did the alarm go off?" has an answer.
                    voice?.RecordEvent($"observer {(command.Kind == ObserverCommandKind.InjectFault ? "injected" : "cleared")} fault {command.Argument}");
                    Debug.Log($"[Observer] {command.Kind} {command.Argument}");
                    break;
                case ObserverCommandKind.HintStep:
                    Hint(command.Argument);
                    break;
                case ObserverCommandKind.SendMessage:
                    voice?.RecordEvent($"observer said: {command.Argument}");
                    voice?.Narrate(Lang.F("Message from your supervisor: {0}", command.Argument), interrupt: false);
                    break;
            }
        }

        private void Hint(string text)
        {
            var step = machine.Runner?.CurrentStep;
            if (step == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(step.PartId) && machine.TryGetPart(step.PartId, out var part))
            {
                highlighter?.Highlight(part, machine.Catalog.DisplayName(step.PartId), duration: 8f);
            }

            voice?.RecordEvent($"observer hinted the step '{step.Title}'");
            voice?.Narrate(string.IsNullOrEmpty(text)
                ? Lang.F("Hint from your supervisor: {0}", Lang.T(step.Title))
                : Lang.F("Hint from your supervisor: {0}", text), interrupt: false);
        }
    }
}
