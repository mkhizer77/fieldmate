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
            LogDataSources();
            var wanted = ModalityDecision.Wanted(Current, HandsTracked(), ControllersTracked());
            // Two polls in a row (1 s) before switching: controllers lying nearby stay tracked while the hands come up,
            // and the two flip-flopped every second on device (2026-09-30).
            pendingPolls = wanted == lastWanted ? pendingPolls + 1 : 1;
            lastWanted = wanted;
            if (pendingPolls >= 2)
            {
                Set(wanted);
            }
        }

        private Modality lastWanted;
        private bool leftFromController;
        private bool rightFromController;

        // Which hands are posed from a held controller (#80): logged on change, to tell finger issues from data issues.
        private void LogDataSources()
        {
            var l = HandDataSource.IsFromController(UnityEngine.XR.Hands.Handedness.Left);
            var r = HandDataSource.IsFromController(UnityEngine.XR.Hands.Handedness.Right);
            if (l != leftFromController || r != rightFromController)
            {
                leftFromController = l;
                rightFromController = r;
                Debug.Log($"[Presence] hand data: left {(l ? "controller" : "cameras")}, right {(r ? "controller" : "cameras")}");
            }
        }
        private int pendingPolls;

        private static bool HandsTracked()
        {
            foreach (var device in InputSystem.devices)
            {
                // Hands posed from a held controller (#80) are shown, but the user is on controllers: they don't count.
                if (device is TrackedDevice tracked && device.layout.Contains("MetaAimHand") && tracked.isTracked.isPressed
                    && !HandDataSource.IsFromController(IsLeft(device) ? UnityEngine.XR.Hands.Handedness.Left : UnityEngine.XR.Hands.Handedness.Right))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLeft(InputDevice device)
        {
            foreach (var usage in device.usages)
            {
                if (usage == CommonUsages.LeftHand)
                {
                    return true;
                }
            }

            return false;
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

    /// <summary>The rule, kept pure for tests: tracked hands win; controllers only without hands; otherwise stay.</summary>
    public static class ModalityDecision
    {
        public static Modality Wanted(Modality current, bool handsTracked, bool controllersTracked) =>
            handsTracked ? Modality.Hands : controllersTracked ? Modality.Controllers : current;
    }
}
