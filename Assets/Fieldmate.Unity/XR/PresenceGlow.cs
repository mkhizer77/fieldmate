using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.XR
{
    /// <summary>
    /// Drives the glow around a hand or controller (shader Fieldmate/PresenceGlow): a quiet halo at rest, brighter when
    /// the matching interactor is over something it can take, brightest while it holds it. One property block, no
    /// allocations; nothing is written while the value is settled. A hand holding a controller (#80, its pose comes from
    /// the controller) also gets a soft translucent fill, the way Meta's home draws hands on controllers.
    /// </summary>
    public sealed class PresenceGlow : MonoBehaviour
    {
        private const float Speed = 6f;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FillId = Shader.PropertyToID("_Fill");

        /// <summary>Fill opacity while the hand holds a controller.</summary>
        public const float HoldingFill = 0.35f;

        [SerializeField] private Renderer target;
        [SerializeField] private XRBaseInteractor interactor;
        [SerializeField] private float idle = 0.9f;
        [SerializeField] private float hover = 1.3f;
        [SerializeField] private float grab = 1.9f;

        private MaterialPropertyBlock block;
        private TrackedHandMesh runtimeMesh;
        private float current;
        private float fill;
        private UnityEngine.XR.Hands.XRHandTrackingEvents handEvents;

        public float Intensity => current;

        /// <summary>The hand's fill opacity now (0 free, <see cref="HoldingFill"/> on a controller).</summary>
        public float Fill => fill;

        public void Configure(Renderer glowRenderer, XRBaseInteractor source)
        {
            target = glowRenderer;
            interactor = source;
            if (block != null)
            {
                Apply(); // configured after Awake (tests, runtime mesh)
            }
        }

        /// <summary>Switches to the runtime hand mesh's renderer once it is built.</summary>
        public void FollowRuntimeMesh(TrackedHandMesh mesh) => runtimeMesh = mesh;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            handEvents = GetComponent<UnityEngine.XR.Hands.XRHandTrackingEvents>();
            current = idle;
            Apply();
        }

        private void Update()
        {
            if (runtimeMesh != null && runtimeMesh.IsBuilt && target != runtimeMesh.Renderer)
            {
                target = runtimeMesh.Renderer;
                Apply();
            }

            var wanted = interactor == null ? idle : interactor.hasSelection ? grab : interactor.hasHover ? hover : idle;
            var wantedFill = handEvents != null && HandDataSource.IsFromController(handEvents.handedness) ? HoldingFill : 0f;
            if (Mathf.Approximately(current, wanted) && Mathf.Approximately(fill, wantedFill))
            {
                return;
            }

            current = Mathf.MoveTowards(current, wanted, Speed * Time.deltaTime);
            fill = Mathf.MoveTowards(fill, wantedFill, 2f * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (target == null)
            {
                return;
            }

            target.GetPropertyBlock(block);
            if (target.sharedMaterial != null && target.sharedMaterial.HasProperty(IntensityId))
            {
                block.SetFloat(IntensityId, current);
                block.SetFloat(FillId, fill);
            }
            else if (target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId))
            {
                var color = target.sharedMaterial.GetColor(BaseColorId);
                color.a = Mathf.Clamp01(0.45f * current); // the grip ring: dimmer at rest, solid while grabbing
                block.SetColor(BaseColorId, color);
            }

            target.SetPropertyBlock(block);
        }
    }
}
