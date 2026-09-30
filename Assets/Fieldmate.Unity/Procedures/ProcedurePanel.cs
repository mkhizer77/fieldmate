using System.Globalization;
using System.Text;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// The step card above the machine (design.md §5.2 StepPresenter / DebriefPanel): a procedure eyebrow, a step chip
    /// with segmented progress, the step title and instruction, and a status row for hints (amber), safety violations
    /// (red) and completed steps (green) that fades out; the debrief takes the same card. Built once with the UI kit;
    /// stays on the machine and turns to face the user.
    /// </summary>
    public sealed class ProcedurePanel : MonoBehaviour
    {
        public static readonly Color Hint = Theme.Warning;
        public static readonly Color Violation = Theme.Danger;
        public static readonly Color Done = Theme.Success;

        private const float WidthMm = 640f;
        private const float HeightMm = 380f;
        private const float StatusFadeSeconds = 0.25f;

        [SerializeField] private Transform head;

        private TMP_Text eyebrowText;
        private TMP_Text chipText;
        private Image chipPill;
        private ProgressSegments progress;
        private TMP_Text titleText;
        private TMP_Text bodyText;
        private TMP_Text statusText;
        private Image statusBar;
        private CanvasGroup statusGroup;
        private float statusUntil;
        private float statusTarget;

        public string TitleText => titleText != null ? titleText.text : string.Empty;
        public string BodyText => bodyText != null ? bodyText.text : string.Empty;
        public string StatusText => statusText != null ? statusText.text : string.Empty;
        public string ChipText => chipText != null ? chipText.text : string.Empty;
        public int ProgressDone => progress != null ? progress.Done : 0;
        public int ProgressTotal => progress != null ? progress.Total : 0;

        private void Awake() => Build();

        public void SetHead(Transform headTransform) => head = headTransform;

        /// <summary>Before a run: setup (placement) or the ready prompt. <paramref name="chip"/> names the phase.</summary>
        public void ShowIdle(string title, string body, string chip = "Setup", string eyebrow = null)
        {
            eyebrowText.text = eyebrow ?? string.Empty;
            SetChip(chip, Theme.SurfaceRaised, Theme.TextSecondary);
            progress.Clear();
            titleText.text = title;
            bodyText.text = body;
            ShowStatus(string.Empty, Color.white, 0f);
        }

        public void ShowStep(int number, int total, string title, string instruction, string procedureTitle = null)
        {
            if (procedureTitle != null)
            {
                eyebrowText.text = procedureTitle;
            }

            SetChip($"Step {number} of {total}", Theme.AccentSoft, Theme.Accent);
            progress.Set(number - 1, total);
            titleText.text = $"Step {number} of {total}: {title}";
            bodyText.text = instruction;
        }

        /// <summary>Shows a status line for <paramref name="seconds"/> (0 = until replaced).</summary>
        public void ShowStatus(string text, Color color, float seconds)
        {
            statusText.text = text ?? string.Empty;
            statusText.color = color;
            statusBar.color = color;
            statusUntil = seconds > 0f ? Time.time + seconds : float.PositiveInfinity;
            statusTarget = statusText.text.Length > 0 ? 1f : 0f;
        }

        public void ShowDebrief(ProcedureResult result, string procedureTitle)
        {
            eyebrowText.text = "Debrief";
            SetChip(result.Passed ? "Passed" : "Not passed", result.Passed ? Theme.Success : Theme.Danger, Theme.TextOnAccent);
            progress.Set(progress.Total, progress.Total, current: false);
            titleText.text = procedureTitle;
            bodyText.text = DebriefText(result);
            ShowStatus(result.Passed ? "Well done." : "Review the notes above and run it again.", result.Passed ? Done : Hint, 0f);
        }

        /// <summary>Score, time and counts on one line, then up to four notes (violations first).</summary>
        public static string DebriefText(ProcedureResult result)
        {
            var sb = new StringBuilder();
            sb.Append("<size=").Append((Theme.Display * 0.6f).ToString(CultureInfo.InvariantCulture)).Append("><color=").Append(Theme.AccentHex).Append('>')
                .Append(result.Score).Append("</color></size><color=").Append(Theme.MutedHex).Append("> / 100</color>")
                .Append("<color=").Append(Theme.SecondaryHex).Append(">   ·   Time ").Append(FormatTime(result.TotalSeconds))
                .Append("   ·   Errors ").Append(result.Errors.Count)
                .Append("   ·   Safety ").Append(result.Violations.Count)
                .Append("   ·   Help ").Append(result.HelpRequests).Append("</color>");
            var shown = 0;
            foreach (var violation in result.Violations)
            {
                if (shown++ == 4) return sb.ToString();
                sb.Append("\n<color=").Append(Theme.DangerHex).Append(">•  ").Append(violation.Description).Append("</color>");
            }

            foreach (var error in result.Errors)
            {
                if (shown++ == 4) return sb.ToString();
                sb.Append("\n<color=").Append(Theme.SecondaryHex).Append(">•  ").Append(error.Message).Append("</color>");
            }

            return sb.ToString();
        }

        public static string FormatTime(double seconds)
        {
            var total = (int)System.Math.Round(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

        private void LateUpdate()
        {
            if (!string.IsNullOrEmpty(statusText.text) && Time.time > statusUntil)
            {
                statusTarget = 0f;
            }

            statusGroup.alpha = Mathf.MoveTowards(statusGroup.alpha, statusTarget, Time.deltaTime / StatusFadeSeconds);
            if (statusTarget == 0f && statusGroup.alpha == 0f && !string.IsNullOrEmpty(statusText.text))
            {
                statusText.text = string.Empty;
            }

            if (head != null)
            {
                UiKit.FaceAway(transform, head.position);
            }
        }

        private void SetChip(string text, Color fill, Color textColor)
        {
            chipText.text = text;
            chipText.color = textColor;
            chipPill.color = fill;
        }

        private void Build()
        {
            UiKit.WorldCanvas(gameObject, WidthMm, HeightMm);
            var card = UiKit.Card("Background", transform, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;
            var pad = new Vector2(Theme.Pad, 0f);

            // Header row: procedure name left, phase / step chip right.
            eyebrowText = UiKit.Eyebrow("Eyebrow", card, new Vector2(0f, 0.875f), new Vector2(0.66f, 0.965f), Theme.TextMuted);
            eyebrowText.rectTransform.offsetMin = pad;
            chipText = UiKit.Chip("Chip", card, new Vector2(0.68f, 0.888f), new Vector2(1f, 0.952f), Theme.SurfaceRaised, Theme.TextSecondary, out chipPill);
            chipPill.rectTransform.offsetMax = -pad;

            var bar = UiKit.Rect("Progress", card, new Vector2(0f, 0.83f), new Vector2(1f, 0.848f), pad, -pad);
            progress = bar.gameObject.AddComponent<ProgressSegments>();

            titleText = UiKit.Label("Title", card, new Vector2(0f, 0.615f), new Vector2(1f, 0.81f), Theme.Title, Theme.TextPrimary,
                TextAlignmentOptions.BottomLeft, semiBold: true, pad, -pad);
            titleText.lineSpacing = -8f;
            bodyText = UiKit.Label("Body", card, new Vector2(0f, 0.20f), new Vector2(1f, 0.595f), Theme.Body, Theme.TextSecondary,
                TextAlignmentOptions.TopLeft, semiBold: false, pad, -pad);
            bodyText.lineSpacing = 4f;
            bodyText.overflowMode = TextOverflowModes.Ellipsis;

            // Status row: coloured bar + text (two lines at most), faded in and out through a CanvasGroup.
            var status = UiKit.Rect("Status", card, new Vector2(0f, 0.04f), new Vector2(1f, 0.185f), pad, -pad);
            statusGroup = status.gameObject.AddComponent<CanvasGroup>();
            statusGroup.alpha = 0f;
            statusBar = UiKit.Bar("StatusBar", status, Vector2.zero, new Vector2(0f, 1f), Hint, Vector2.zero, new Vector2(6f, 0f));
            statusText = UiKit.Label("StatusText", status, Vector2.zero, Vector2.one, Theme.Caption, Hint,
                TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(Theme.Gap + 6f, 0f), Vector2.zero);
            statusText.lineSpacing = -6f;
            statusText.text = string.Empty; // a fresh TMP label reports null text
        }
    }
}
