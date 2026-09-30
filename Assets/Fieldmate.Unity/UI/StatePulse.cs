using UnityEngine;
using UnityEngine.UI;

namespace Fieldmate.UI
{
    /// <summary>A status dot that breathes while something is happening (listening, thinking, speaking) and sits still otherwise.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class StatePulse : MonoBehaviour
    {
        private const float Period = 1.4f;
        private Image image;
        private Color color = Theme.TextMuted;

        public bool IsPulsing { get; private set; }

        public void Set(Color dotColor, bool pulsing)
        {
            color = dotColor;
            IsPulsing = pulsing;
            Apply(1f);
        }

        private void Awake()
        {
            image = GetComponent<Image>();
            Apply(1f);
        }

        private void Update()
        {
            if (IsPulsing)
            {
                Apply(0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2f * Mathf.PI / Period)));
            }
        }

        private void Apply(float brightness)
        {
            if (image == null)
            {
                return;
            }

            var c = color;
            c.a = brightness;
            image.color = c;
            image.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.1f, brightness);
        }
    }
}
