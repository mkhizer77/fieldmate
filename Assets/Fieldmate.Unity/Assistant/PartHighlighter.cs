using Fieldmate.Twin;
using UnityEngine;
using UnityEngine.UI;

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

        [SerializeField] private Color highlight = new(0.1f, 0.9f, 1f, 1f);
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
        private Text labelText;
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
                if (markerMaterial == null && material != null)
                {
                    markerMaterial = new Material(material);
                    markerMaterial.SetColor(BaseColor, highlight);
                    diamond.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                    stem.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                }
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
                var away = label.position - viewer.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                {
                    label.rotation = Quaternion.LookRotation(away, Vector3.up);
                }
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

            var canvasGo = new GameObject("Label", typeof(RectTransform), typeof(Canvas));
            label = canvasGo.transform;
            label.SetParent(marker, false);
            label.localPosition = Vector3.up * 0.09f;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)label;
            rect.sizeDelta = new Vector2(600f, 90f);
            rect.localScale = Vector3.one * 0.001f;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(label, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            labelText = textGo.GetComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 56;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = highlight;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            textGo.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

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
