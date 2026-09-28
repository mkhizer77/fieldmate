using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace Fieldmate.XR
{
    /// <summary>
    /// Watches whether Touch controllers are tracked; if not, the user is on hands. Polls twice a second (no per-frame
    /// work) and raises <see cref="Changed"/> so instructions and hints switch wording. The interactors themselves are
    /// bound to both (controller grip/trigger and the hand's index pinch), so input already works either way.
    /// </summary>
    public sealed class InputModalityProbe : MonoBehaviour
    {
        private const float PollSeconds = 0.5f;

        private float nextPoll;

        public static Modality Current { get; private set; } = Modality.Hands;

        public static event Action<Modality> Changed;

        /// <summary>Sets the modality directly (tests, or a debug toggle).</summary>
        public static void Set(Modality modality)
        {
            if (Current == modality)
            {
                return;
            }

            Current = modality;
            Debug.Log($"[Input] now using {modality}");
            Changed?.Invoke(modality);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll)
            {
                return;
            }

            nextPoll = Time.unscaledTime + PollSeconds;
            Set(ControllersTracked() ? Modality.Controllers : Modality.Hands);
        }

        private static bool ControllersTracked()
        {
            foreach (var device in InputSystem.devices)
            {
                // Touch / Touch Plus layouts; hand devices (MetaAimHand, HandInteraction) are also XR controllers, skip them.
                if (device is XRController controller && device.layout.Contains("Touch") && controller.isTracked.isPressed)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
