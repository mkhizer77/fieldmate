using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// A small label floating above a control so it is obvious what can be operated and what it does, e.g.
    /// "Main breaker · motor power · ON". The state follows the control (<see cref="IMachineControl.StateReached"/>).
    /// Faces the user around the vertical axis; built at runtime (one small world-space canvas).
    /// </summary>
    public sealed class ControlTag : MonoBehaviour
    {
        [SerializeField] private string title;
        [SerializeField] private string purpose;
        [SerializeField] private Vector3 offset = new(0f, 0.12f, 0f);

        private IMachineControl control;
        private Transform label;
        private Text text;
        private Camera viewer;

        public string Text => text != null ? text.text : string.Empty;

        public void Configure(string tagTitle, string tagPurpose, Vector3 localOffset)
        {
            title = tagTitle;
            purpose = tagPurpose;
            offset = localOffset;
        }

        private void Awake()
        {
            control = GetComponent<IMachineControl>();
            Build();
            Refresh(control?.State);
        }

        private void OnEnable()
        {
            if (control != null)
            {
                control.StateReached += OnStateReached;
            }
        }

        private void OnDisable()
        {
            if (control != null)
            {
                control.StateReached -= OnStateReached;
            }
        }

        private void OnStateReached(string partId, string state) => Refresh(state);

        private void Refresh(string state) => text.text = ControlTagText.Format(title, purpose, state);

        private void LateUpdate()
        {
            // Stays above the control's rest position (not the moving handle), turned towards the user.
            label.position = transform.parent != null ? transform.parent.TransformPoint(transform.localPosition + offset) : transform.position + offset;
            if (viewer == null)
            {
                viewer = Camera.main;
            }

            if (viewer != null)
            {
                var away = label.position - viewer.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                {
                    label.rotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }
        }

        private void Build()
        {
            var canvasGo = new GameObject($"{name} Tag", typeof(RectTransform), typeof(Canvas));
            label = canvasGo.transform;
            label.SetParent(transform.parent != null ? transform.parent : transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)label;
            rect.sizeDelta = new Vector2(420f, 110f);
            rect.localScale = Vector3.one * 0.0006f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(label, false);
            Stretch((RectTransform)background.transform);
            background.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.75f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(label, false);
            Stretch((RectTransform)textGo.transform);
            text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
