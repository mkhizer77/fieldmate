using Fieldmate.Twin;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Pulses a part's colour so the user can find it. Uses one MaterialPropertyBlock and cached renderers, so the pulse
    /// allocates nothing per frame.
    /// </summary>
    public sealed class PartHighlighter : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color highlight = new(0.1f, 0.9f, 1f, 1f);
        [SerializeField] private float seconds = 6f;

        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private Color[] originals;
        private float until;

        public string ActivePartId { get; private set; }

        private void Awake() => block = new MaterialPropertyBlock();

        public void Highlight(PartTag part)
        {
            Clear();
            ActivePartId = part.PartId;
            renderers = part.GetComponentsInChildren<Renderer>();
            originals = new Color[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                originals[i] = renderers[i].sharedMaterial != null && renderers[i].sharedMaterial.HasProperty(BaseColor)
                    ? renderers[i].sharedMaterial.GetColor(BaseColor)
                    : Color.white;
            }

            until = Time.time + seconds;
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
                block.SetColor(BaseColor, Color.Lerp(originals[i], highlight, t));
                renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
