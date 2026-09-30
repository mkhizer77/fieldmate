using Fieldmate.Twin;
using Fieldmate.UI;
using TMPro;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Makes a part easy to find: its colour pulses strongly and a cyan marker with the part's name bobs above it, so it
    /// can be spotted even when the part is small or at the edge of view. Uses one MaterialPropertyBlock, cached
    /// renderers and a marker built once, so the pulse allocates nothing per frame.
    /// </summary>
    public sealed class PartHighlighter : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color highlight = Theme.Accent;
        [SerializeField] private float seconds = 12f;
        [SerializeField] private float markerHeight = 0.22f;

        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private Color[] originals;
        private float until;

        private Transform marker;
        private Transform diamond;
        private Transform stem;
        private Transform label;
        private TMP_Text labelText;
        private Material markerMaterial;
        private Vector3 partTop;
        private Camera viewer;

        public string ActivePartId { get; private set; }

        /// <summary>The floating marker; active while a part is highlighted.</summary>
        public Transform Marker => marker;

        public string MarkerLabel => labelText != null ? labelText.text : string.Empty;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            BuildMarker();
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

            partTop = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            labelText.text = string.IsNullOrEmpty(displayName) ? part.PartId : displayName;
            marker.gameObject.SetActive(true);
            until = Time.time + (duration > 0f ? duration : seconds);
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
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (markerMaterial != null)
            {
                Destroy(markerMaterial);
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

            var t = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            for (var i = 0; i < renderers.Length; i++)
            {
                block.SetColor(BaseColor, Color.Lerp(originals[i], highlight, 0.35f + 0.65f * t));
                renderers[i].SetPropertyBlock(block);
            }

            PlaceMarker();
        }

        private void PlaceMarker()
        {
            var bob = 0.03f * Mathf.Sin(Time.time * 3f);
            var tip = partTop + Vector3.up * (markerHeight + bob);
            marker.position = tip;
            diamond.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);

            // Stem from the diamond down to the top of the part.
            var length = Mathf.Max(0.01f, tip.y - partTop.y - 0.04f);
            stem.localScale = new Vector3(0.008f, length * 0.5f, 0.008f);
            stem.localPosition = new Vector3(0f, -0.04f - length * 0.5f, 0f);

            if (viewer == null)
            {
                viewer = Camera.main;
            }

            if (viewer != null)
            {
                UiKit.FaceAway(label, viewer.transform.position);
            }
        }

        private void BuildMarker()
        {
            marker = new GameObject("Highlight Marker").transform;
            marker.SetParent(transform, false);

            diamond = Primitive(PrimitiveType.Cube, "Diamond");
            diamond.localScale = Vector3.one * 0.05f;
            diamond.localRotation = Quaternion.Euler(45f, 0f, 45f);
            stem = Primitive(PrimitiveType.Cylinder, "Stem");
            // Drawn on top of the machine (no depth test), so the marker never sinks into a pipe above the part.
            var overlay = Shader.Find("Fieldmate/UnlitOverlay");
            markerMaterial = new Material(overlay != null ? overlay : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Highlight Marker" };
            markerMaterial.SetColor(BaseColor, highlight);
            diamond.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            stem.GetComponent<Renderer>().sharedMaterial = markerMaterial;

            var canvasGo = new GameObject("Label", typeof(RectTransform));
            label = canvasGo.transform;
            label.SetParent(marker, false);
            label.localPosition = Vector3.up * 0.1f;
            UiKit.WorldCanvas(canvasGo, 420f, 116f); // name + a short "what to do" line
            var pill = UiKit.Card("Pill", label, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), Theme.Scrim).transform;
            labelText = UiKit.Label("Text", pill, Vector2.zero, Vector2.one, 36f, highlight, TextAlignmentOptions.Center, semiBold: true);
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Overflow;
            labelText.lineSpacing = -8f;

            marker.gameObject.SetActive(false);
        }

        private Transform Primitive(PrimitiveType type, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>()); // the marker must never catch gaze rays
            go.transform.SetParent(marker, false);
            return go.transform;
        }
    }
}
