using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Fieldmate.Net
{
    /// <summary>
    /// The observer app's single screen (#26, macOS): join by code, then watch the trainee's twin, procedure and
    /// conversation live, and send faults, hints and messages. Built in code like the rest of the UI; everything shown
    /// comes from the trainee through <see cref="ObserverClient"/>.
    /// </summary>
    public sealed class ObserverDashboard : MonoBehaviour
    {
        private const float StaleSeconds = 2f;
        private static readonly Color Page = new(0.035f, 0.04f, 0.055f, 1f);

        [SerializeField] private ObserverNetwork network;

        private ObserverClient client;
        private CancellationTokenSource joining;
        private bool dirty;
        private float nextStaleCheck;

        private TMP_InputField codeField;
        private TMP_Text joinLabel;
        private TMP_Text status;
        private readonly TMP_Text[] values = new TMP_Text[TelemetryModel.ChannelCount];
        private readonly Image[] tiles = new Image[TelemetryModel.ChannelCount];
        private TMP_Text machineLine;
        private TMP_Text procedure;
        private TMP_Text transcript;
        private TMP_Text result;
        private TMP_InputField hintField;
        private TMP_InputField messageField;
        private RectTransform faultList;
        private readonly List<(string id, Button button, TMP_Text label)> faultButtons = new();

        public ObserverClient Client => client;
        public string StatusText => status != null ? status.text : string.Empty;
        public string ProcedureText => procedure != null ? procedure.text : string.Empty;
        public string TranscriptText => transcript != null ? transcript.text : string.Empty;

        public void Configure(ObserverNetwork observerNetwork) => network = observerNetwork;

        private void Awake() => Build();

        private void OnDestroy()
        {
            joining?.Cancel();
            Leave();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextStaleCheck)
            {
                nextStaleCheck = Time.unscaledTime + 0.5f;
                dirty = true; // the "last update" age moves even when nothing arrives
            }

            if (dirty)
            {
                dirty = false;
                Refresh();
            }
        }

        public async void Join(string code)
        {
            if (client != null || joining != null)
            {
                Leave();
                return;
            }

            joining = new CancellationTokenSource();
            status.text = $"Connecting to {code}…";
            joinLabel.text = "Cancel";
            try
            {
                await network.StartClientAsync(code, joining.Token);
                Attach(network.Link);
            }
            catch (Exception e)
            {
                status.text = $"Couldn't connect: {e.Message}";
                joinLabel.text = "Join";
                network.Shutdown();
            }
            finally
            {
                joining?.Dispose();
                joining = null;
            }

            dirty = true;
        }

        /// <summary>Watches whatever is on the other end of <paramref name="link"/> (the network, or the preview).</summary>
        public void Attach(INetLink link)
        {
            client?.Dispose();
            client = new ObserverClient(link) { Clock = () => Time.unscaledTimeAsDouble };
            client.Changed += () => dirty = true;
            joinLabel.text = "Leave";
            dirty = true;
        }

        public void Leave()
        {
            joining?.Cancel();
            client?.Dispose();
            client = null;
            network?.Shutdown();
            if (joinLabel != null)
            {
                joinLabel.text = "Join";
            }

            foreach (var (_, button, _) in faultButtons)
            {
                Destroy(button.gameObject);
            }

            faultButtons.Clear();
            dirty = true;
        }

        public void Send(ObserverCommand command)
        {
            if (client == null || client.Send(command) == 0)
            {
                result.text = "Not connected.";
            }
        }

        private void Refresh()
        {
            if (client == null)
            {
                if (joining == null)
                {
                    status.text = "Not connected. Enter the code shown in the headset (hand menu → Observer).";
                }

                return;
            }

            if (client.Incompatible)
            {
                status.text = "The headset runs another version of Fieldmate; update both apps.";
                return;
            }

            var age = Time.unscaledTimeAsDouble - client.LastSnapshotTime;
            status.text = !client.Connected ? (client.Hello == null ? "Waiting for the headset…" : "Disconnected from the headset.")
                : !client.HasSnapshot ? "Connected, waiting for data…"
                : age > StaleSeconds ? $"{client.Hello?.MachineName}: no update for {age:0} s"
                : $"{client.Hello?.MachineName}: live";

            if (client.Hello != null && faultButtons.Count != client.Hello.Faults.Count)
            {
                BuildFaultButtons();
            }

            if (!client.HasSnapshot)
            {
                return;
            }

            var s = client.Snapshot;
            for (var i = 0; i < values.Length; i++)
            {
                var spec = TelemetryModel.Spec((TelemetryChannel)i);
                values[i].text = $"<size=60%>{(TelemetryChannel)i}</size>\n{s.Telemetry[i].ToString("0.0", CultureInfo.InvariantCulture)} <size=60%>{spec.Unit}</size>";
                tiles[i].color = s.Status[i] switch
                {
                    ChannelStatus.Alarm => new Color(0.45f, 0.08f, 0.08f, 1f),
                    ChannelStatus.Warning => new Color(0.42f, 0.30f, 0.05f, 1f),
                    _ => new Color(0.09f, 0.11f, 0.14f, 1f),
                };
            }

            machineLine.text = $"Power {(s.Powered ? "ON" : "off")}   Inlet {s.Inlet * 100f:0} %   Outlet {s.Outlet * 100f:0} %";
            procedure.text = DescribeProcedure(s);

            foreach (var (id, _, label) in faultButtons)
            {
                var name = client.Hello.Faults.Find(f => f.Id == id).Name;
                label.text = client.IsFaultActive(id) ? $"<color=#FF6B61>●</color> {name}: clear" : $"{name}: inject";
            }

            var sb = new StringBuilder();
            var lines = client.Transcript;
            for (var i = Math.Max(0, lines.Count - 30); i < lines.Count; i++)
            {
                var line = lines[i];
                sb.Append(line.Kind switch
                {
                    TranscriptKind.User => "<color=#9FD3FF>Trainee:</color> ",
                    TranscriptKind.Assistant => "<color=#7CE3A0>Mate:</color> ",
                    TranscriptKind.Tool => "<color=#8A93A0>Tool:</color> ",
                    TranscriptKind.Error => "<color=#FF6B61>Error:</color> ",
                    _ => "<color=#8A93A0>Info:</color> ",
                }).Append(Escape(line.Text)).Append('\n');
            }

            transcript.text = sb.ToString();
            if (client.LastResult is { } last)
            {
                result.text = (last.Accepted ? "✓ " : "✗ ") + Escape(last.Message);
            }
        }

        public static string DescribeProcedure(ObserverSnapshot s)
        {
            switch (s.Procedure)
            {
                case RunnerState.NotStarted:
                    return $"<b>{Escape(s.ProcedureTitle)}</b>\nNot started.";
                case RunnerState.Completed:
                    return $"<b>{Escape(s.ProcedureTitle)}</b>\nCompleted: score {s.Score} ({(s.Passed ? "passed" : "not passed")})\n" +
                           $"Errors {s.Errors} · Safety violations {s.Violations} · Help {s.HelpRequests}";
                default:
                    return $"<b>{Escape(s.ProcedureTitle)}</b>\nStep {s.StepIndex + 1} of {s.StepCount}: {Escape(s.StepTitle)}\n" +
                           $"Errors {s.Errors} · Safety violations {s.Violations} · Help {s.HelpRequests}\n" +
                           $"Session {TimeSpan.FromSeconds(s.SessionSeconds):mm\\:ss}";
            }
        }

        // The trainee's text is shown with TMP rich text on; brackets would let a line restyle the dashboard.
        private static string Escape(string text) => (text ?? string.Empty).Replace("<", "‹").Replace(">", "›");

        private void BuildFaultButtons()
        {
            foreach (var (_, button, _) in faultButtons)
            {
                Destroy(button.gameObject);
            }

            faultButtons.Clear();
            var row = 0;
            foreach (var (id, _) in client.Hello.Faults)
            {
                var faultId = id;
                var button = MakeButton(faultList, $"Fault {id}", new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -40f - row * 46f), new Vector2(0f, -row * 46f), out var label,
                    () => Send(client.IsFaultActive(faultId) ? ObserverCommand.ClearFault(faultId) : ObserverCommand.InjectFault(faultId)));
                faultButtons.Add((id, button, label));
                row++;
            }
        }

        private void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            var canvasGo = new GameObject("Observer Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;
            Panel(root, "Page", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Page);

            // Top bar: title, code, join.
            Text(root, "Title", new Vector2(0f, 1f), new Vector2(0.3f, 1f), new Vector2(24f, -64f), new Vector2(0f, -16f), 30f, "Fieldmate Observer", bold: true);
            codeField = Field(root, "Code", new Vector2(0.3f, 1f), new Vector2(0.62f, 1f), new Vector2(0f, -62f), new Vector2(0f, -18f), "Code from the headset, e.g. K7Q2XB or 192.168.1.20:7788");
            MakeButton(root, "Join", new Vector2(0.62f, 1f), new Vector2(0.72f, 1f), new Vector2(12f, -62f), new Vector2(0f, -18f), out joinLabel,
                () => Join(codeField.text));
            joinLabel.text = "Join";
            status = Text(root, "Status", new Vector2(0.72f, 1f), new Vector2(1f, 1f), new Vector2(20f, -64f), new Vector2(-24f, -16f), 18f, string.Empty);

            // Left: telemetry, machine inputs, faults.
            var left = Panel(root, "Twin", new Vector2(0f, 0f), new Vector2(0.3f, 1f), new Vector2(24f, 84f), new Vector2(-12f, -84f), Theme.Surface);
            Text(left, "Twin Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -44f), new Vector2(-16f, -10f), 16f, "TWIN", bold: true);
            for (var i = 0; i < values.Length; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var tile = Panel(left, $"Tile {i}", new Vector2(col * 0.5f, 1f), new Vector2(col * 0.5f + 0.5f, 1f),
                    new Vector2(col == 0 ? 16f : 6f, -160f - row * 116f), new Vector2(col == 0 ? -6f : -16f, -52f - row * 116f), Color.gray);
                tiles[i] = tile.GetComponent<Image>();
                values[i] = Text(tile, "Value", Vector2.zero, Vector2.one, new Vector2(12f, 8f), new Vector2(-12f, -8f), 34f, "–", bold: true);
            }

            machineLine = Text(left, "Inputs", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -320f), new Vector2(-16f, -284f), 17f, string.Empty);
            Text(left, "Faults Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -366f), new Vector2(-16f, -332f), 16f, "FAULTS", bold: true);
            faultList = Rect(left, "Faults", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 16f), new Vector2(-16f, -376f));

            // Centre: procedure and hint.
            var centre = Panel(root, "Procedure", new Vector2(0.3f, 0.45f), new Vector2(0.62f, 1f), new Vector2(12f, 0f), new Vector2(-12f, -84f), Theme.Surface);
            Text(centre, "Procedure Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -44f), new Vector2(-16f, -10f), 16f, "PROCEDURE", bold: true);
            procedure = Text(centre, "Procedure Text", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 70f), new Vector2(-16f, -52f), 22f, string.Empty);
            hintField = Field(centre, "Hint", new Vector2(0f, 0f), new Vector2(0.72f, 0f), new Vector2(16f, 16f), new Vector2(0f, 58f), "Optional hint text");
            MakeButton(centre, "Hint Button", new Vector2(0.72f, 0f), new Vector2(1f, 0f), new Vector2(10f, 16f), new Vector2(-16f, 58f), out var hintLabel,
                () => Send(ObserverCommand.HintStep(hintField.text)));
            hintLabel.text = "Hint step";

            // Centre bottom: command results.
            var results = Panel(root, "Results", new Vector2(0.3f, 0f), new Vector2(0.62f, 0.45f), new Vector2(12f, 84f), new Vector2(-12f, -12f), Theme.Surface);
            Text(results, "Results Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -44f), new Vector2(-16f, -10f), 16f, "LAST COMMAND", bold: true);
            result = Text(results, "Result", Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -52f), 18f, string.Empty);

            // Right: transcript and message.
            var right = Panel(root, "Conversation", new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Vector2(12f, 84f), new Vector2(-24f, -84f), Theme.Surface);
            Text(right, "Conversation Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -44f), new Vector2(-16f, -10f), 16f, "CONVERSATION", bold: true);
            var mask = Rect(right, "Transcript Mask", Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -52f));
            mask.gameObject.AddComponent<RectMask2D>();
            transcript = Text(mask, "Transcript", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 16f, string.Empty);
            transcript.alignment = TextAlignmentOptions.BottomLeft; // newest at the bottom, oldest clipped at the top
            transcript.overflowMode = TextOverflowModes.Overflow;

            messageField = Field(root, "Message", new Vector2(0.62f, 0f), new Vector2(0.9f, 0f), new Vector2(12f, 24f), new Vector2(0f, 68f), "Message to speak in the headset");
            MakeButton(root, "Send Button", new Vector2(0.9f, 0f), new Vector2(1f, 0f), new Vector2(10f, 24f), new Vector2(-24f, 68f), out var sendLabel, () =>
            {
                Send(ObserverCommand.SendMessage(messageField.text));
                messageField.text = string.Empty;
            });
            sendLabel.text = "Send";
            Refresh();
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rect = Rect(parent, name, min, max, offsetMin, offsetMax);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static TMP_Text Text(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, float size, string value, bool bold = false)
        {
            var text = Rect(parent, name, min, max, offsetMin, offsetMax).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = bold ? Theme.SemiBold : Theme.Regular;
            text.fontSize = size;
            text.color = Theme.TextPrimary;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.richText = true;
            text.raycastTarget = false;
            text.text = value ?? string.Empty;
            return text;
        }

        private static TMP_InputField Field(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, string placeholder)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.name = name;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var field = go.GetComponent<TMP_InputField>();
            field.characterLimit = ObserverProtocol.MaxTextChars;
            field.fontAsset = Theme.Regular;
            field.pointSize = 18f;
            ((TMP_Text)field.placeholder).text = placeholder;
            ((TMP_Text)field.placeholder).color = Theme.TextMuted;
            field.textComponent.color = Theme.TextPrimary;
            field.caretColor = Theme.Accent;
            go.GetComponent<Image>().color = Theme.ButtonMuted;
            return field;
        }

        private static Button MakeButton(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax,
            out TMP_Text label, Action onClick)
        {
            var rect = Panel(parent, name, min, max, offsetMin, offsetMax, Theme.ButtonSecondary);
            rect.GetComponent<Image>().raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => onClick());
            label = Text(rect, "Label", Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f), 17f, name);
            label.alignment = TextAlignmentOptions.Midline;
            return button;
        }
    }
}
