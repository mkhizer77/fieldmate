using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Fieldmate.XR
{
    /// <summary>
    /// The pointer ray's look: a light white line with a dot at its end; the moment the user pinches or pulls the
    /// trigger it turns blue (trying to press), and back to white on release. The dot follows the line's last point,
    /// so it sits on the button when the ray hits one. No allocations after Awake.
    /// </summary>
    [RequireComponent(typeof(XRRayInteractor), typeof(XRInteractorLineVisual), typeof(LineRenderer))]
    public sealed class PointerRayStyle : MonoBehaviour
    {
        public static readonly Color Idle = new(1f, 1f, 1f, 0.55f);
        public static readonly Color Pressing = new(0.21f, 0.62f, 1f, 0.95f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Transform dot;

        private XRRayInteractor ray;
        private XRInteractorLineVisual visual;
        private LineRenderer line;
        private Renderer dotRenderer;
        private MaterialPropertyBlock block;
        private Gradient idleGradient;
        private Gradient pressingGradient;
        private bool pressing;

        public bool IsPressing => pressing;
        public Color CurrentColor => pressing ? Pressing : Idle;

        public void Configure(Transform endDot) => dot = endDot;

        private void Awake()
        {
            ray = GetComponent<XRRayInteractor>();
            visual = GetComponent<XRInteractorLineVisual>();
            line = GetComponent<LineRenderer>();
            block = new MaterialPropertyBlock();
            idleGradient = Solid(Idle);
            pressingGradient = Solid(Pressing);
            dotRenderer = dot != null ? dot.GetComponent<Renderer>() : null;
            Apply(false);
        }

        private void Update()
        {
            var now = ray.isSelectActive;
            if (now != pressing)
            {
                Apply(now);
            }
        }

        private void LateUpdate()
        {
            if (dot == null || line.positionCount == 0)
            {
                return;
            }

            dot.gameObject.SetActive(line.enabled);
            dot.position = line.GetPosition(line.positionCount - 1);
        }

        /// <summary>White at rest, blue while the select input is held (also used by tests).</summary>
        public void Apply(bool isPressing)
        {
            pressing = isPressing;
            var gradient = isPressing ? pressingGradient : idleGradient;
            visual.setLineColorGradient = true;
            visual.validColorGradient = gradient;
            visual.invalidColorGradient = gradient;
            if (dotRenderer != null)
            {
                dotRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, CurrentColor);
                dotRenderer.SetPropertyBlock(block);
            }
        }

        private static Gradient Solid(Color color)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 1f) });
            return g;
        }
    }
}
