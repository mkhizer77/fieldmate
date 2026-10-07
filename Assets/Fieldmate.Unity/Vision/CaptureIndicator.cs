using Fieldmate.UI;
using Fieldmate.XR;
using TMPro;
using UnityEngine;

namespace Fieldmate.Vision
{
    /// <summary>
    /// The privacy indicator (design.md §5.5, #23): a red "Camera" pill fixed in view for a few seconds after every frame
    /// the app takes, so it's always clear when an image was captured. It listens to <see cref="CameraFrameSource.Captured"/>,
    /// which fires for every successful capture and nothing else. Hidden, it costs no draw calls; no per-frame allocations.
    /// </summary>
    public sealed class CaptureIndicator : MonoBehaviour
    {
        public const float Seconds = 2.5f;
        private const float FadeSeconds = 0.4f;
        private const float WidthMm = 74f;
        private const float HeightMm = 20f;
        private const float UnitsPerMm = 4f;

        [SerializeField] private CameraFrameSource source;
        [SerializeField] private Transform head;
        [Tooltip("Where the pill sits in head space: a little above the centre of view.")]
        [SerializeField] private Vector3 offset = new(0f, 0.085f, 0.55f);

        private Canvas canvas;
        private CanvasGroup group;
        private float shownAt = float.NegativeInfinity;

        public bool IsShown => canvas != null && canvas.enabled;

        /// <summary>How many captures were indicated (tests, logs).</summary>
        public int Count { get; private set; }

        public void Configure(CameraFrameSource frameSource, Transform headTransform)
        {
            source = frameSource;
            head = headTransform;
        }

        private void Awake() => Build();

        private void OnEnable()
        {
            if (source != null)
            {
                source.Captured += OnCaptured;
            }
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.Captured -= OnCaptured;
            }
        }

        private void OnCaptured(CameraFrame frame)
        {
            Count++;
            shownAt = Time.unscaledTime;
            canvas.enabled = true;
            group.alpha = 1f;
            Follow();
        }

        private void LateUpdate()
        {
            if (!canvas.enabled)
            {
                return;
            }

            var age = Time.unscaledTime - shownAt;
            if (age >= Seconds)
            {
                canvas.enabled = false;
                return;
            }

            group.alpha = Mathf.Clamp01((Seconds - age) / FadeSeconds);
            Follow();
        }

        // Head-locked: the pill moves with the view so it can't be missed.
        private void Follow()
        {
            if (head == null)
            {
                return;
            }

            canvas.transform.SetPositionAndRotation(head.TransformPoint(offset), head.rotation);
        }

        private void Build()
        {
            var go = new GameObject("Camera Indicator", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = UiKit.WorldCanvas(go, WidthMm, HeightMm, UnitsPerMm);
            group = go.GetComponent<CanvasGroup>();
            UiKit.Card("Pill", go.transform, Vector2.zero, Vector2.one, new Color(0.35f, 0.04f, 0.04f, 0.92f), stroke: true);
            UiKit.Card("Dot", go.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Theme.Danger, false,
                new Vector2(9f * UnitsPerMm - 18f, -18f), new Vector2(9f * UnitsPerMm + 18f, 18f));
            var text = UiKit.Label("Text", go.transform, Vector2.zero, Vector2.one, 11f * UnitsPerMm, Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, semiBold: true, new Vector2(17f * UnitsPerMm, 0f), Vector2.zero);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = "Camera";
            canvas.enabled = false;
        }
    }
}
