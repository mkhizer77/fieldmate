using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Fieldmate.XR
{
    /// <summary>
    /// Asks the runtime to suppress the Guardian boundary (XR_META_boundary_visibility): a passthrough app that fills the
    /// room must not vanish when the user steps outside the stationary circle to reach a part (device test 2026-09-30).
    /// The runtime only allows this once passthrough is rendering, so the request is retried for a while.
    /// </summary>
    public sealed class BoundaryControl : MonoBehaviour
    {
        private const float RetrySeconds = 2f;
        private const float GiveUpSeconds = 60f;

        private BoundaryVisibilityFeature feature;
        private float nextTry;
        private bool done;

        public bool Suppressed => feature != null && feature.currentVisibility == XrBoundaryVisibility.VisibilitySuppressed;

        private void Start()
        {
            feature = OpenXRSettings.Instance != null ? OpenXRSettings.Instance.GetFeature<BoundaryVisibilityFeature>() : null;
            if (feature == null || !feature.enabled)
            {
                Debug.Log("[Boundary] visibility feature not available; the Guardian stays on");
                done = true;
            }
        }

        private void Update()
        {
            if (done || Time.unscaledTime < nextTry)
            {
                return;
            }

            nextTry = Time.unscaledTime + RetrySeconds;
            if (Suppressed)
            {
                Debug.Log("[Boundary] suppressed");
                done = true;
                return;
            }

            var result = feature.TryRequestBoundaryVisibility(XrBoundaryVisibility.VisibilitySuppressed);
            // NOT_ALLOWED and RuntimeFailure both happen before passthrough renders (device 2026-09-30: RuntimeFailure at
            // launch); keep asking for a minute, then leave the Guardian as it is.
            if ((int)result == BoundaryVisibilityFeature.XR_BOUNDARY_VISIBILITY_SUPPRESSION_NOT_ALLOWED_META || (int)result < 0)
            {
                if (Time.unscaledTime > GiveUpSeconds)
                {
                    Debug.LogWarning($"[Boundary] runtime kept refusing ({result}); the Guardian stays on");
                    done = true;
                }
            }
        }
    }
}
