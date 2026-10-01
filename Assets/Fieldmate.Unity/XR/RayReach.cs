using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.XR
{
    /// <summary>
    /// What the pointer ray can act on (device test 2026-10-01): with controllers it reaches the machine's controls
    /// (turn a valve, pull the cover, carry the cartridge) from where the user stands, as in Meta's apps; with hands it
    /// only presses buttons and parts are operated by reaching in. Follows <see cref="InputModalityProbe"/>.
    /// </summary>
    [RequireComponent(typeof(XRRayInteractor))]
    public sealed class RayReach : MonoBehaviour
    {
        [SerializeField] private InteractionLayerMask buttons = 1 << 1;
        [SerializeField] private InteractionLayerMask controls = 1; // Default: valves, breaker, cover, cartridge

        private XRRayInteractor ray;

        public InteractionLayerMask Current => ray != null ? ray.interactionLayers : buttons;

        public void Configure(InteractionLayerMask buttonLayer, InteractionLayerMask controlLayer)
        {
            buttons = buttonLayer;
            controls = controlLayer;
        }

        private void Awake()
        {
            ray = GetComponent<XRRayInteractor>();
            ray.allowAnchorControl = false; // the thumbstick rotates and resizes during placement; don't push held parts
        }

        private void OnEnable()
        {
            InputModalityProbe.Changed += Apply;
            Apply(InputModalityProbe.Current);
        }

        private void OnDisable() => InputModalityProbe.Changed -= Apply;

        private void Apply(Modality modality)
        {
            InteractionLayerMask reach = modality == Modality.Controllers ? (int)(buttons | controls) : (int)buttons;
            if (ray.interactionLayers != reach)
            {
                // Drop whatever it holds before it can no longer reach it.
                if (ray.hasSelection && modality != Modality.Controllers && ray.interactionManager != null)
                {
                    ray.interactionManager.CancelInteractorSelection((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)ray);
                }

                ray.interactionLayers = reach;
            }
        }
    }
}
