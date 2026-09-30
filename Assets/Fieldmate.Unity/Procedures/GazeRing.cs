using Fieldmate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// A progress ring over the part the user is looking at during a look or reading step: it fills as the dwell
    /// accumulates, so the user sees the gaze registering (device test 2026-09-30: "it doesn't feel like it's doing
    /// anything"). Drawn on top of the machine, turned to the viewer; hidden when the gaze is elsewhere.
    /// </summary>
    public sealed class GazeRing : MonoBehaviour
    {
        private const float SizeMm = 64f;
        private const float StandOff = 0.06f; // metres in front of the part, toward the viewer

        private Transform head;
        private Image fill;
        private CanvasGroup group;

        public bool IsShowing => group != null && group.alpha > 0f;
        public float Progress => fill != null ? fill.fillAmount : 0f;

        public static GazeRing Create(Transform headTransform)
        {
            var go = new GameObject("Gaze Ring", typeof(RectTransform));
            var ringUi = go.AddComponent<GazeRing>();
            ringUi.head = headTransform;
            ringUi.Build();
            return ringUi;
        }

        /// <summary>Floats the ring just in front of a part (its bounds centre and radius) toward the viewer.</summary>
        public void ShowInFront(Vector3 partCenter, float partRadius, float progress)
        {
            var toViewer = head != null ? (head.position - partCenter).normalized : Vector3.back;
            Show(partCenter + toViewer * (partRadius + StandOff), progress);
        }

        /// <summary>Places the ring at <paramref name="worldPosition"/> with <paramref name="progress"/> (0–1) filled.</summary>
        public void Show(Vector3 worldPosition, float progress)
        {
            transform.position = worldPosition;
            if (head != null)
            {
                UiKit.FaceAway(transform, head.position);
            }

            fill.fillAmount = Mathf.Clamp01(progress);
            group.alpha = 1f;
        }

        public void Hide()
        {
            if (group.alpha != 0f)
            {
                group.alpha = 0f;
                fill.fillAmount = 0f;
            }
        }

        private void Build()
        {
            UiKit.WorldCanvas(gameObject, SizeMm, SizeMm);
            group = GetComponent<CanvasGroup>();
            var track = UiKit.Rect("Track", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            RingImage(track, new Color(1f, 1f, 1f, 0.18f));
            fill = UiKit.Rect("Fill", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            RingImage(fill, Theme.Accent);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            group.alpha = 0f;
        }

        private static void RingImage(Image image, Color color)
        {
            image.sprite = UiKit.Ring;
            image.color = color;
            image.material = UiKit.ImageOverlay;
            image.raycastTarget = false;
        }
    }
}
