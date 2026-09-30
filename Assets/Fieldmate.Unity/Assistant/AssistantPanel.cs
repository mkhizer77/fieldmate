using System.Collections.Generic;
using System.Text;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The assistant's panel beside the machine: a status dot that breathes while it listens, thinks or speaks, the
    /// transcript with tool calls, a detail card for the manual section, step or log the assistant opened, and a toast
    /// banner when it is offline. Built once with the UI kit; pinned to the machine, turned to face the user.
    /// </summary>
    public sealed class AssistantPanel : MonoBehaviour
    {
        private const int MaxLines = 9;
        private const float WidthMm = 620f;
        private const float HeightMm = 560f;

        [SerializeField] private Transform head;
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset = new(1.15f, 1.45f, 0.25f);

        private readonly Queue<string> lines = new();
        private TMP_Text stateText;
        private TMP_Text transcriptText;
        private TMP_Text detailEyebrow;
        private TMP_Text detailText;
        private TMP_Text bannerText;
        private GameObject banner;
        private StatePulse pulse;

        public string DetailText => detailText != null ? detailText.text : string.Empty;
        public string TranscriptText => transcriptText != null ? transcriptText.text : string.Empty;
        public string StateLabel => stateText != null ? stateText.text : string.Empty;
        public bool IsPulsing => pulse != null && pulse.IsPulsing;

        private void Awake() => Build();

        public void SetHead(Transform headTransform) => head = headTransform;

        public void SetState(AssistantState state, string hint)
        {
            var (label, color, busy) = state switch
            {
                AssistantState.Listening => ("Listening…", Theme.Success, true),
                AssistantState.Transcribing => ("Transcribing…", Theme.Warning, true),
                AssistantState.Thinking => ("Thinking…", Theme.Warning, true),
                AssistantState.Speaking => ("Speaking…", Theme.Accent, true),
                _ => (hint, Theme.TextMuted, false),
            };
            stateText.text = label;
            stateText.color = busy ? Theme.TextPrimary : Theme.TextSecondary;
            pulse.Set(color, busy);
        }

        public void Add(TranscriptEntry entry)
        {
            var line = entry.Kind switch
            {
                TranscriptKind.User => $"<color={Theme.SecondaryHex}>You</color>   {entry.Text}",
                TranscriptKind.Assistant => $"<color={Theme.AccentHex}>Fieldmate</color>   {entry.Text}",
                TranscriptKind.Tool => $"<size={Theme.Eyebrow}><color={Theme.MutedHex}>→ {entry.Text}</color></size>",
                TranscriptKind.Error => $"<color={Theme.DangerHex}>{entry.Text}</color>",
                _ => $"<i><color={Theme.SecondaryHex}>{entry.Text}</color></i>",
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
            banner.SetActive(!string.IsNullOrEmpty(text));
        }

        public void ShowSection(ManualSection section) => ShowDetail("From the manual", $"<b>[{section.Id}] {section.Title}</b>\n{section.Text}");

        public void ShowStep(ProcedureDefinition procedure, int number)
        {
            var step = procedure.Steps[number - 1];
            ShowDetail("Procedure", $"<b>{procedure.Title}</b>\nStep {number} of {procedure.Steps.Count}: {step.Title}");
        }

        public void ShowNotes(IReadOnlyList<string> notes)
        {
            var sb = new StringBuilder("<b>Maintenance log</b>");
            foreach (var note in notes)
            {
                sb.Append("\n•  ").Append(note);
            }

            ShowDetail("Maintenance log", sb.ToString());
        }

        /// <summary>Pins the panel beside the machine: it moves with the machine, never with the head.</summary>
        public void Anchor(Transform machine, Vector3 localOffset)
        {
            anchor = machine;
            anchorOffset = localOffset;
        }

        private void ShowDetail(string eyebrow, string text)
        {
            detailEyebrow.text = eyebrow;
            detailText.text = text;
        }

        // Stays put beside the machine (device test: a head-following panel was in the way); only turns to stay readable.
        private void LateUpdate()
        {
            if (anchor != null)
            {
                transform.position = anchor.TransformPoint(anchorOffset);
            }

            if (head != null)
            {
                UiKit.FaceAway(transform, head.position);
            }
        }

        private void Build()
        {
            UiKit.WorldCanvas(gameObject, WidthMm, HeightMm);
            var card = UiKit.Card("Background", transform, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;
            var pad = new Vector2(Theme.Pad, 0f);

            // Header: status dot + state, wordmark on the right, hairline below.
            var dot = UiKit.Rect("StateDot", card, new Vector2(0f, 0.935f), new Vector2(0f, 0.935f),
                new Vector2(Theme.Pad, -11f), new Vector2(Theme.Pad + 22f, 11f)).gameObject;
            var dotImage = dot.AddComponent<Image>();
            dotImage.sprite = UiKit.Rounded;
            dotImage.type = Image.Type.Simple;
            dotImage.material = UiKit.ImageOverlay;
            dotImage.raycastTarget = false;
            pulse = dot.AddComponent<StatePulse>();
            stateText = UiKit.Label("State", card, new Vector2(0f, 0.875f), new Vector2(1f, 0.99f), Theme.Caption + 2f, Theme.TextSecondary,
                TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(Theme.Pad + 40f, 0f), new Vector2(-(Theme.Pad + 150f), 0f));
            stateText.lineSpacing = -6f;
            var wordmark = UiKit.Eyebrow("Wordmark", card, new Vector2(1f, 0.875f), new Vector2(1f, 0.99f), Theme.TextMuted, TextAlignmentOptions.MidlineRight);
            wordmark.rectTransform.offsetMin = new Vector2(-(Theme.Pad + 150f), 0f);
            wordmark.rectTransform.offsetMax = -pad;
            wordmark.text = "Fieldmate";
            UiKit.Bar("Divider", card, new Vector2(0f, 0.87f), new Vector2(1f, 0.87f), Theme.Stroke, new Vector2(Theme.Pad, -1f), new Vector2(-Theme.Pad, 1f));

            // Transcript: bottom-aligned and allowed to overflow upwards inside a clipping viewport, so the newest line
            // always shows and the oldest scroll off the top (Truncate would drop the newest lines once answers wrap).
            var viewport = UiKit.Rect("TranscriptViewport", card, new Vector2(0f, 0.35f), new Vector2(1f, 0.855f), pad, -pad);
            viewport.gameObject.AddComponent<RectMask2D>();
            transcriptText = UiKit.Label("Transcript", viewport, Vector2.zero, Vector2.one, Theme.Body - 2f, Theme.TextPrimary, TextAlignmentOptions.BottomLeft);
            transcriptText.overflowMode = TextOverflowModes.Overflow;
            transcriptText.lineSpacing = 6f;
            transcriptText.paragraphSpacing = 14f;

            // Detail card: what the assistant opened (manual section, step, log).
            var detail = UiKit.Card("DetailBox", card, new Vector2(0f, 0.035f), new Vector2(1f, 0.325f), Theme.SurfaceRaised, false, pad, -pad).transform;
            var inset = new Vector2(Theme.Gap + 8f, 0f);
            detailEyebrow = UiKit.Eyebrow("DetailEyebrow", detail, new Vector2(0f, 0.78f), new Vector2(1f, 0.96f), Theme.TextMuted);
            detailEyebrow.rectTransform.offsetMin = inset;
            detailEyebrow.text = "Ask about the machine";
            detailText = UiKit.Label("Detail", detail, new Vector2(0f, 0.06f), new Vector2(1f, 0.78f), Theme.Caption, Theme.TextSecondary,
                TextAlignmentOptions.TopLeft, semiBold: false, inset, -inset);

            // Offline toast above the panel.
            banner = UiKit.Card("Banner", transform, new Vector2(0f, 1.02f), new Vector2(1f, 1.13f), new Color(0.55f, 0.15f, 0.12f, 0.95f)).gameObject;
            bannerText = UiKit.Label("BannerText", banner.transform, Vector2.zero, Vector2.one, Theme.Caption, Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(Theme.Gap + 8f, 0f), new Vector2(-Theme.Gap, 0f));
            banner.SetActive(false);
        }
    }
}
