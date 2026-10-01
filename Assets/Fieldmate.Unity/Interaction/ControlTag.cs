using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// A pill floating above a control so it is obvious what can be operated and what it does, e.g.
    /// "Main breaker · motor power · ON", with a thin stem down to the control. The state follows the control
    /// (<see cref="IMachineControl.StateReached"/>) and is coloured by meaning (energised / neutral / secured). Faces the
    /// user around the vertical axis and fades when far away so the machine isn't wallpapered with labels.
    /// </summary>
    public sealed class ControlTag : MonoBehaviour
    {
        private const float CanvasScale = 0.0006f;
        private const float WidthUnits = 420f;
        private const float HeightUnits = 110f;
        private const float NearMetres = 1.6f;
        private const float FarMetres = 3.5f;
        private const float FarAlpha = 0.25f;

        [SerializeField] private string title;
        [SerializeField] private string purpose;
        [SerializeField] private Vector3 offset = new(0f, 0.12f, 0f);

        private IMachineControl control;
        private Transform label;
        private CanvasGroup group;
        private TMP_Text text;
        private string plainText = string.Empty;
        private Camera viewer;

        /// <summary>The tag's content without colour markup: title, then "purpose · STATE".</summary>
        public string Text => plainText ?? string.Empty;

        public float Alpha => group != null ? group.alpha : 1f;

        /// <summary>While the assistant highlights this part, its callout takes the tag's place (#71).</summary>
        public bool Suppressed { get; set; }

        /// <summary>Where the pill floats (world), for a callout that replaces it.</summary>
        public Vector3 LabelPosition => label != null ? label.position : transform.position + offset;

        /// <summary>Who the tag faces and fades for; defaults to the main camera.</summary>
        public void SetViewer(Camera camera) => viewer = camera;

        public void Configure(string tagTitle, string tagPurpose, Vector3 localOffset)
        {
            title = tagTitle;
            purpose = tagPurpose;
            offset = localOffset;
            if (text != null)
            {
                Refresh(control?.State); // configured after Awake (tests)
            }
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

        private void Refresh(string state)
        {
            plainText = ControlTagText.Format(title, purpose, state);
            text.text = ControlTagText.Rich(title, purpose, state);
        }

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
                UiKit.FaceAway(label, viewer.transform.position);
                var distance = Vector3.Distance(label.position, viewer.transform.position);
                group.alpha = Suppressed ? 0f : Mathf.Lerp(1f, FarAlpha, Mathf.InverseLerp(NearMetres, FarMetres, distance));
            }
        }

        private void Build()
        {
            var canvasGo = new GameObject($"{name} Tag", typeof(RectTransform));
            label = canvasGo.transform;
            label.SetParent(transform.parent != null ? transform.parent : transform, false);
            UiKit.WorldCanvas(canvasGo, WidthUnits * CanvasScale * 1000f, HeightUnits * CanvasScale * 1000f, 1f / (CanvasScale * 1000f));
            group = canvasGo.GetComponent<CanvasGroup>();

            // Stem from the pill down to the control, in canvas units (the canvas scale is the same in every axis).
            var stemLength = Mathf.Max(0f, offset.y / CanvasScale - HeightUnits * 0.5f);
            if (stemLength > 0f)
            {
                UiKit.Bar("Stem", label, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.Stroke, new Vector2(-1.5f, -stemLength), new Vector2(1.5f, 0f));
                var foot = UiKit.Card("Foot", label, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.TextSecondary, false,
                    new Vector2(-7f, -stemLength - 7f), new Vector2(7f, -stemLength + 7f));
                foot.type = Image.Type.Simple;
            }

            var pill = UiKit.Card("Pill", label, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;
            text = UiKit.Label("Text", pill, Vector2.zero, Vector2.one, 32f, Theme.TextPrimary, TextAlignmentOptions.Center, semiBold: true,
                new Vector2(Theme.Gap, 0f), new Vector2(-Theme.Gap, 0f));
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.lineSpacing = -6f;
        }
    }
}
