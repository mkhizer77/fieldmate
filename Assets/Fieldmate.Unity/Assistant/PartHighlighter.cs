using Fieldmate.Twin;
using Fieldmate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Makes a part easy to find: a soft colour pulse on the part, a focus ring around it facing the user, and a callout
    /// pill above it in the control-tag style (name, and what to do), joined by a short stem. Everything draws over the
    /// machine and sits at the part itself, not above whatever the part is mounted on. One MaterialPropertyBlock and
    /// cached renderers, so the pulse allocates nothing per frame.
    /// </summary>
    public sealed class PartHighlighter : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private const float CanvasScale = 0.0006f;
        private const float PillWidth = 420f;
        private const float PillHeight = 110f;
        private const float RingPadding = 0.03f;
        private const float PillGap = 0.05f;

        [SerializeField] private Color highlight = Theme.Accent;
        [SerializeField] private float seconds = 12f;

        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private Color[] originals;
        private float until;

        private Transform focus;
        private RectTransform focusRect;
        private Transform marker;
        private TMP_Text labelText;
        private Vector3 partCenter;
        private float partRadius;
        private float partTop;
        private Camera viewer;

        public string ActivePartId { get; private set; }

        /// <summary>The callout pill above the part; active while a part is highlighted.</summary>
        public Transform Marker => marker;

        public string MarkerLabel => labelText != null ? labelText.text : string.Empty;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            Build();
        }

        /// <param name="duration">Seconds to keep it; defaults to the configured time. Use infinity for a step's part.</param>
        public void Highlight(PartTag part, string displayName = null, float duration = -1f)
        {
            Clear();
            ActivePartId = part.PartId;
            renderers = part.GetComponentsInChildren<Renderer>();
            originals = new Color[renderers.Length];
            var bounds = new Bounds(part.transform.position, Vector3.zero);
            for (var i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                originals[i] = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
                bounds.Encapsulate(renderers[i].bounds);
            }

            partCenter = bounds.center;
            partRadius = Mathf.Max(0.04f, bounds.extents.magnitude);
            partTop = bounds.max.y;
            labelText.text = string.IsNullOrEmpty(displayName) ? part.PartId : displayName;
            focus.gameObject.SetActive(true);
            marker.gameObject.SetActive(true);
            var diameter = (partRadius + RingPadding) * 2f / CanvasScale;
            focusRect.sizeDelta = new Vector2(diameter, diameter);
            until = Time.time + (duration > 0f ? duration : seconds);
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

            focus.localScale = Vector3.one * (CanvasScale * (1f + 0.06f * t));
            Place();
        }

        private void Place()
        {
            if (viewer == null)
            {
                viewer = Camera.main;
            }

            focus.position = partCenter;
            marker.position = new Vector3(partCenter.x, partTop + PillGap + PillHeight * CanvasScale * 0.5f, partCenter.z);
            if (viewer != null)
            {
                UiKit.FaceAway(focus, viewer.transform.position);
                UiKit.FaceAway(marker, viewer.transform.position);
            }
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

            // Callout pill above the part, tag style: name in the accent colour, action line below.
            var markerGo = new GameObject("Highlight Callout", typeof(RectTransform));
            marker = markerGo.transform;
            marker.SetParent(transform, false);
            UiKit.WorldCanvas(markerGo, PillWidth * CanvasScale * 1000f, PillHeight * CanvasScale * 1000f, 1f / (CanvasScale * 1000f));
            var stemLength = (PillGap + RingPadding) / CanvasScale;
            UiKit.Bar("Stem", marker, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Theme.AccentSoft, new Vector2(-2f, -stemLength), new Vector2(2f, 0f));
            var pill = UiKit.Card("Pill", marker, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;
            UiKit.Bar("Accent", pill, new Vector2(0f, 0.2f), new Vector2(0f, 0.8f), highlight, new Vector2(Theme.Gap, 0f), new Vector2(Theme.Gap + 5f, 0f));
            labelText = UiKit.Label("Text", pill, Vector2.zero, Vector2.one, 32f, highlight, TextAlignmentOptions.Center, semiBold: true,
                new Vector2(Theme.Gap + 14f, 0f), new Vector2(-Theme.Gap, 0f));
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Overflow;
            labelText.lineSpacing = -6f;

            focus.gameObject.SetActive(false);
            marker.gameObject.SetActive(false);
        }
    }
}
