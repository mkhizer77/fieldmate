using System.Collections.Generic;
using System.Text;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Head-following panel: assistant state (listening / thinking / speaking), transcript with tool calls, the manual
    /// section or step the assistant opened, and an offline banner. Built at runtime with uGUI (no prefab needed).
    /// </summary>
    public sealed class AssistantPanel : MonoBehaviour
    {
        private const int MaxLines = 9;

        [SerializeField] private Transform head;
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset = new(1.15f, 1.45f, 0.25f);

        private readonly Queue<string> lines = new();
        private Text stateText;
        private Text transcriptText;
        private Text detailText;
        private Text bannerText;
        private Image stateDot;

        public string DetailText => detailText != null ? detailText.text : string.Empty;
        public string TranscriptText => transcriptText != null ? transcriptText.text : string.Empty;
        public string StateLabel => stateText != null ? stateText.text : string.Empty;

        private void Awake() => Build();

        public void SetHead(Transform headTransform) => head = headTransform;

        public void SetState(AssistantState state, string hint)
        {
            var (label, color) = state switch
            {
                AssistantState.Listening => ("Listening…", new Color(0.2f, 0.9f, 0.3f)),
                AssistantState.Transcribing => ("Transcribing…", new Color(1f, 0.8f, 0.2f)),
                AssistantState.Thinking => ("Thinking…", new Color(1f, 0.8f, 0.2f)),
                AssistantState.Speaking => ("Speaking…", new Color(0.3f, 0.7f, 1f)),
                _ => (hint, new Color(0.6f, 0.6f, 0.6f)),
            };
            stateText.text = label;
            stateDot.color = color;
        }

        public void Add(TranscriptEntry entry)
        {
            var line = entry.Kind switch
            {
                TranscriptKind.User => $"<b>You:</b> {entry.Text}",
                TranscriptKind.Assistant => $"<b><color=#7fd4ff>Fieldmate:</color></b> {entry.Text}",
                TranscriptKind.Tool => $"<size=18><color=#9a9a9a>⚙ {entry.Text}</color></size>",
                TranscriptKind.Error => $"<color=#ff6b6b>{entry.Text}</color>",
                _ => $"<i><color=#c8c8c8>{entry.Text}</color></i>",
            };
            lines.Enqueue(line);
            while (lines.Count > MaxLines)
            {
                lines.Dequeue();
            }

            transcriptText.text = string.Join("\n", lines);
        }

        public void ShowBanner(string text)
        {
            bannerText.text = text ?? string.Empty;
            bannerText.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void ShowSection(ManualSection section) => detailText.text = $"<b>[{section.Id}] {section.Title}</b>\n{section.Text}";

        public void ShowStep(ProcedureDefinition procedure, int number)
        {
            var step = procedure.Steps[number - 1];
            detailText.text = $"<b>{procedure.Title}</b>\nStep {number} of {procedure.Steps.Count}: {step.Title}";
        }

        public void ShowNotes(IReadOnlyList<string> notes)
        {
            var sb = new StringBuilder("<b>Maintenance log</b>");
            foreach (var note in notes)
            {
                sb.Append("\n• ").Append(note);
            }

            detailText.text = sb.ToString();
        }

        /// <summary>Pins the panel beside the machine: it moves with the machine, never with the head.</summary>
        public void Anchor(Transform machine, Vector3 localOffset)
        {
            anchor = machine;
            anchorOffset = localOffset;
        }

        // Stays put beside the machine (device test: a head-following panel was in the way); only turns to stay readable.
        private void LateUpdate()
        {
            if (anchor != null)
            {
                transform.position = anchor.TransformPoint(anchorOffset);
            }

            if (head == null)
            {
                return;
            }

            var away = transform.position - head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f)
            {
                transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(620f, 560f);
            rect.localScale = Vector3.one * 0.001f;

            var background = Panel("Background", transform, Vector2.zero, Vector2.one, new Color(0.05f, 0.06f, 0.08f, 0.82f));
            stateDot = Panel("StateDot", background, new Vector2(0.03f, 0.92f), new Vector2(0.06f, 0.96f), Color.grey).GetComponent<Image>();
            stateText = Label("State", background, new Vector2(0.08f, 0.9f), new Vector2(0.97f, 0.98f), 26, TextAnchor.MiddleLeft);
            // Bottom-aligned and allowed to overflow upwards inside a clipping viewport: the newest line always shows and
            // the oldest scroll off the top. (Truncate would drop the newest lines once long answers wrap.)
            var viewport = new GameObject("TranscriptViewport", typeof(RectTransform), typeof(RectMask2D)).transform;
            viewport.SetParent(background, false);
            Stretch((RectTransform)viewport, new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.89f));
            transcriptText = Label("Transcript", viewport, Vector2.zero, Vector2.one, 21, TextAnchor.LowerLeft);
            transcriptText.verticalOverflow = VerticalWrapMode.Overflow;
            detailText = Label("Detail", Panel("DetailBox", background, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.34f),
                new Color(1f, 1f, 1f, 0.06f)), new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), 19, TextAnchor.UpperLeft);
            var banner = Panel("Banner", transform, new Vector2(0f, 1.01f), new Vector2(1f, 1.1f), new Color(0.55f, 0.15f, 0.1f, 0.9f));
            bannerText = Label("BannerText", banner, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 18, TextAnchor.MiddleLeft);
            banner.gameObject.SetActive(false);
        }

        private static Transform Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform, min, max);
            go.GetComponent<Image>().color = color;
            return go.transform;
        }

        private static Text Label(string name, Transform parent, Vector2 min, Vector2 max, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform, min, max);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = anchor;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
