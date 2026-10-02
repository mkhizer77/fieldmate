using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.XR
{
    /// <summary>
    /// Drives the glow around a hand or controller (shader Fieldmate/PresenceGlow): a quiet halo at rest, brighter when
    /// the matching interactor is over something it can take, brightest while it holds it. One property block, no
    /// allocations; nothing is written while the value is settled.
    /// </summary>
    public sealed class PresenceGlow : MonoBehaviour
    {
        private const float Speed = 6f;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer target;
        [SerializeField] private XRBaseInteractor interactor;
        [SerializeField] private float idle = 0.9f;
        [SerializeField] private float hover = 1.3f;
        [SerializeField] private float grab = 1.9f;

        private MaterialPropertyBlock block;
        private TrackedHandMesh runtimeMesh;
        private float current;

        public float Intensity => current;

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
            if (Mathf.Approximately(current, wanted))
            {
                return;
            }

            current = Mathf.MoveTowards(current, wanted, Speed * Time.deltaTime);
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
