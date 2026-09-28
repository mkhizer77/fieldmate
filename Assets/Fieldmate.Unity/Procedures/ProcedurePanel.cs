using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// The step card above the machine (design.md §5.2 StepPresenter / DebriefPanel): current step, an instruction, a
    /// status line for hints (amber), safety violations (red) and completed steps (green), and the debrief when the
    /// procedure is done. It stays on the machine and turns to face the user. Built at runtime with uGUI.
    /// </summary>
    public sealed class ProcedurePanel : MonoBehaviour
    {
        public static readonly Color Hint = new(1f, 0.78f, 0.25f);
        public static readonly Color Violation = new(1f, 0.35f, 0.3f);
        public static readonly Color Done = new(0.4f, 0.95f, 0.5f);

        [SerializeField] private Transform head;

        private Text titleText;
        private Text bodyText;
        private Text statusText;
        private Image background;
        private float statusUntil;

        public string TitleText => titleText != null ? titleText.text : string.Empty;
        public string BodyText => bodyText != null ? bodyText.text : string.Empty;
        public string StatusText => statusText != null ? statusText.text : string.Empty;

        private void Awake() => Build();

        public void SetHead(Transform headTransform) => head = headTransform;

        public void ShowIdle(string title, string body)
        {
            titleText.text = title;
            bodyText.text = body;
            ShowStatus(string.Empty, Color.white, 0f);
        }

        public void ShowStep(int number, int total, string title, string instruction)
        {
            titleText.text = $"Step {number} of {total}: {title}";
            bodyText.text = instruction;
        }

        /// <summary>Shows a status line for <paramref name="seconds"/> (0 = until replaced).</summary>
        public void ShowStatus(string text, Color color, float seconds)
        {
            statusText.text = text ?? string.Empty;
            statusText.color = color;
            statusUntil = seconds > 0f ? Time.time + seconds : float.PositiveInfinity;
        }

        public void ShowDebrief(ProcedureResult result, string procedureTitle)
        {
            titleText.text = $"{procedureTitle}: {(result.Passed ? "passed" : "not passed")}";
            var sb = new StringBuilder();
            sb.Append("Score <b>").Append(result.Score).Append("</b> / 100   ·   Time ")
                .Append(FormatTime(result.TotalSeconds)).Append('\n')
                .Append("Errors ").Append(result.Errors.Count)
                .Append("   ·   Safety violations ").Append(result.Violations.Count)
                .Append("   ·   Help requests ").Append(result.HelpRequests);
            AppendLines(sb, result);
            bodyText.text = sb.ToString();
            ShowStatus(result.Passed ? "Well done." : "Review the notes above and run it again.", result.Passed ? Done : Hint, 0f);
        }

        private static void AppendLines(StringBuilder sb, ProcedureResult result)
        {
            var shown = 0;
            foreach (var violation in result.Violations)
            {
                if (shown++ == 4) return;
                sb.Append("\n<color=#ff6b6b>• ").Append(violation.Description).Append("</color>");
            }

            foreach (var error in result.Errors)
            {
                if (shown++ == 4) return;
                sb.Append("\n• ").Append(error.Message);
            }
        }

        public static string FormatTime(double seconds)
        {
            var total = (int)System.Math.Round(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

        private void LateUpdate()
        {
            if (statusText.text.Length > 0 && Time.time > statusUntil)
            {
                statusText.text = string.Empty;
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
            rect.sizeDelta = new Vector2(640f, 300f);
            rect.localScale = Vector3.one * 0.001f;

            background = Rect("Background", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.06f, 0.08f, 0.85f);
            titleText = Label("Title", background.transform, new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.96f), 28, FontStyle.Bold);
            bodyText = Label("Body", background.transform, new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.74f), 22, FontStyle.Normal);
            statusText = Label("Status", background.transform, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.2f), 20, FontStyle.Bold);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return r;
        }

        private static Text Label(string name, Transform parent, Vector2 min, Vector2 max, int size, FontStyle style)
        {
            var text = Rect(name, parent, min, max).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
