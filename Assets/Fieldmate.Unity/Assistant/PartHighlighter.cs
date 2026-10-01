using Fieldmate.Interaction;
using Fieldmate.Twin;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Makes a part easy to find: a soft colour pulse on the part, a focus ring that hugs the part's outline as the user
    /// sees it, and a callout in exactly the control-tag style (same pill, text, stem and foot) above it. A control that
    /// already has a tag hands its place to the callout while highlighted, so there is one label, where the user is used
    /// to seeing it (#71 device test). Everything draws over the machine. One MaterialPropertyBlock and cached renderers,
    /// so the pulse allocates nothing per frame.
    /// </summary>
    public sealed class PartHighlighter : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // The control tag's dimensions, so the callout is the same object to the eye.
        private const float CanvasScale = 0.0006f;
        private const float PillWidth = 420f;
        private const float PillHeight = 110f;
        private const float TagLift = 0.12f; // above the part's top, like a control's tag offset
        private const float RingPadding = 0.012f;
        private const float RingRefreshSeconds = 0.5f;

        [SerializeField] private Color highlight = Theme.Accent;
        [SerializeField] private float seconds = 12f;

        private readonly Vector3[] corners = new Vector3[8];
        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private Color[] originals;
        private float until;
        private ControlTag replacedTag;

        private Transform focus;
        private RectTransform focusRect;
        private Transform marker;
        private RectTransform stem;
        private RectTransform foot;
        private TMP_Text labelText;
        private string plainLabel = string.Empty;
        private Bounds partBounds;
        private float nextRingRefresh;
        private Camera viewer;

        public string ActivePartId { get; private set; }

        /// <summary>The callout above the part; active while a part is highlighted.</summary>
        public Transform Marker => marker;

        /// <summary>The callout's text without colour markup.</summary>
        public string MarkerLabel => plainLabel;

        /// <summary>The focus ring's diameter in metres (tests).</summary>
        public float RingDiameter => focusRect != null ? focusRect.sizeDelta.x * CanvasScale : 0f;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            Build();
        }

        /// <param name="duration">Seconds to keep it; defaults to the configured time. Use infinity for a step's part.</param>
        public void Highlight(PartTag part, string displayName = null, float duration = -1f) => Highlight(part, displayName, null, duration);

        /// <summary>Highlights a part with a callout titled <paramref name="title"/> and an optional second line.</summary>
        public void Highlight(PartTag part, string title, string detail, float duration)
        {
            Clear();
            ActivePartId = part.PartId;
            renderers = part.GetComponentsInChildren<Renderer>();
            originals = new Color[renderers.Length];
            var bounds = new Bounds(part.transform.position, Vector3.zero);
            var first = true;
            for (var i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                originals[i] = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
                if (first)
                {
                    bounds = renderers[i].bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            partBounds = bounds;
            title = string.IsNullOrEmpty(title) ? part.PartId : title;
            plainLabel = ControlTagText.Format(title, detail, null);
            labelText.text = ControlTagText.Rich(title, detail, null);

            replacedTag = part.GetComponentInChildren<ControlTag>();
            if (replacedTag != null)
            {
                replacedTag.Suppressed = true;
            }

            focus.gameObject.SetActive(true);
            marker.gameObject.SetActive(true);
            until = Time.time + (duration > 0f ? duration : seconds);
            nextRingRefresh = 0f;
            Place();
        }

        public void Clear()
        {
            if (renderers != null)
            {
                foreach (var r in renderers)
                {
                    if (r != null)
                    {
                        r.SetPropertyBlock(null);
                    }
                }
            }

            if (replacedTag != null)
            {
                replacedTag.Suppressed = false;
                replacedTag = null;
            }

            renderers = null;
            ActivePartId = null;
            if (focus != null)
            {
                focus.gameObject.SetActive(false);
                marker.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (renderers == null)
            {
                return;
            }

            if (Time.time > until)
            {
                Clear();
                return;
            }

            var t = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            for (var i = 0; i < renderers.Length; i++)
            {
                block.SetColor(BaseColor, Color.Lerp(originals[i], highlight, 0.2f + 0.3f * t));
                renderers[i].SetPropertyBlock(block);
            }

            focus.localScale = Vector3.one * (CanvasScale * (1f + 0.04f * t));
            Place();
        }

        private void Place()
        {
            if (viewer == null)
            {
                viewer = Camera.main;
            }

            var centre = partBounds.center;
            focus.position = centre;
            var top = partBounds.max.y;
            var pillCentre = replacedTag != null
                ? replacedTag.LabelPosition
                : new Vector3(centre.x, top + TagLift + PillHeight * CanvasScale * 0.5f, centre.z);
            marker.position = pillCentre;

            // Stem and foot from the pill down to the top of the part, like a control's tag.
            var stemUnits = Mathf.Max(0f, (pillCentre.y - PillHeight * CanvasScale * 0.5f - top) / CanvasScale);
            stem.offsetMin = new Vector2(-1.5f, -stemUnits);
            foot.offsetMin = new Vector2(-7f, -stemUnits - 7f);
            foot.offsetMax = new Vector2(7f, -stemUnits + 7f);

            if (viewer == null)
            {
                return;
            }

            UiKit.FaceAway(focus, viewer.transform.position);
            UiKit.FaceAway(marker, viewer.transform.position);
            if (Time.time >= nextRingRefresh)
            {
                nextRingRefresh = Time.time + RingRefreshSeconds;
                var diameter = (VisibleRadius(viewer.transform.position) + RingPadding) * 2f / CanvasScale;
                focusRect.sizeDelta = new Vector2(diameter, diameter);
            }
        }

        // The part's outline as seen from the viewer: the farthest box corner from the centre across the line of sight,
        // trimmed a little because round parts don't fill their box's corners. Not the 3D diagonal (that ringed the
        // neighbours too).
        private float VisibleRadius(Vector3 eye)
        {
            var c = partBounds.center;
            var e = partBounds.extents;
            var view = (c - eye).normalized;
            var i = 0;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                corners[i++] = new Vector3(e.x * x, e.y * y, e.z * z);
            }

            var max = 0f;
            foreach (var corner in corners)
            {
                var across = corner - Vector3.Dot(corner, view) * view;
                max = Mathf.Max(max, across.magnitude);
            }

            return Mathf.Max(0.025f, max * 0.85f);
        }

        private void Build()
        {
            // Focus ring around the part.
            var focusGo = new GameObject("Focus Ring", typeof(RectTransform));
            focus = focusGo.transform;
            focus.SetParent(transform, false);
            UiKit.WorldCanvas(focusGo, 100f, 100f, 1f / (CanvasScale * 1000f));
            focusRect = (RectTransform)focus;
            var ring = UiKit.Rect("Ring", focus, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            ring.sprite = UiKit.Ring;
            ring.color = highlight;
            ring.material = UiKit.ImageOverlay;
            ring.raycastTarget = false;

            // The callout: the control tag's pill, text, stem and foot.
            var markerGo = new GameObject("Highlight Callout", typeof(RectTransform));
            marker = markerGo.transform;
            marker.SetParent(transform, false);
            UiKit.WorldCanvas(markerGo, PillWidth * CanvasScale * 1000f, PillHeight * CanvasScale * 1000f, 1f / (CanvasScale * 1000f));
            stem = UiKit.Bar("Stem", marker, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.Stroke, new Vector2(-1.5f, -60f), new Vector2(1.5f, 0f)).rectTransform;
            var footImage = UiKit.Card("Foot", marker, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.TextSecondary, false,
                new Vector2(-7f, -67f), new Vector2(7f, -53f));
            footImage.type = Image.Type.Simple;
            foot = footImage.rectTransform;
            var pill = UiKit.Card("Pill", marker, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;
            labelText = UiKit.Label("Text", pill, Vector2.zero, Vector2.one, 32f, Theme.TextPrimary, TextAlignmentOptions.Center, semiBold: true,
                new Vector2(Theme.Gap, 0f), new Vector2(-Theme.Gap, 0f));
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Overflow;
            labelText.lineSpacing = -6f;

            focus.gameObject.SetActive(false);
            marker.gameObject.SetActive(false);
        }
    }
}
