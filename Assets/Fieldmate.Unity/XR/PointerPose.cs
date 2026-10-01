using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Fieldmate.XR
{
    /// <summary>
    /// Drives a pointer from the controller's aim pose or the hand's aim pose, whichever the user is using
    /// (<see cref="InputModalityProbe"/>), never a mix. One action bound to both picked whichever control moved more, so
    /// with controllers in hand a still-reporting hand pose could steer the ray (device test 2026-10-01). Reads the
    /// devices' controls directly: an action only learns a value when it changes, and "tracked" that is already true at
    /// launch never changes, which left the ray at the rig's origin on the floor until a hand was lost and found again
    /// (device test 2026-10-01, 13:22). Falls back to the other source when the chosen one isn't tracked; keeps the last
    /// pose when neither is. Updates before the interactors and again just before rendering.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public sealed class PointerPose : MonoBehaviour
    {
        [SerializeField] private string side = "Right";

        // The actions only find the devices (bindings re-resolve when a device appears); values are read from the controls.
        private InputAction controllerTracked;
        private InputAction handTracked;

        private InputDevice cachedDevice;
        private Vector3Control cachedPosition;
        private QuaternionControl cachedRotation;

        /// <summary>Which source drove the pointer last frame (tests, logs).</summary>
        public Modality Source { get; private set; }

        /// <summary>False until a tracked controller or hand has moved the pointer.</summary>
        public bool HasPose { get; private set; }

        public void Configure(string hand) => side = hand;

        private void Awake()
        {
            controllerTracked = new InputAction("Controller tracked", InputActionType.PassThrough, $"<XRController>{{{side}Hand}}/isTracked");
            handTracked = new InputAction("Hand tracked", InputActionType.PassThrough, $"<MetaAimHand>{{{side}Hand}}/isTracked");
        }

        private void OnEnable()
        {
            controllerTracked.Enable();
            handTracked.Enable();
            Application.onBeforeRender += Apply;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= Apply;
            controllerTracked.Disable();
            handTracked.Disable();
        }

        private void OnDestroy()
        {
            controllerTracked.Dispose();
            handTracked.Dispose();
        }

        private void Update() => Apply();

        /// <summary>The active input's source if it is tracked, else whichever is.</summary>
        public static bool UseController(Modality active, bool controllerTracked, bool handTracked) =>
            active == Modality.Controllers ? controllerTracked || !handTracked : controllerTracked && !handTracked;

        private void Apply()
        {
            var controller = TrackedDevice(controllerTracked);
            var hand = TrackedDevice(handTracked);
            if (controller == null && hand == null)
            {
                return; // nothing tracked: keep the last pose
            }

            var useController = UseController(InputModalityProbe.Current, controller != null, hand != null);
            var device = useController ? controller : hand;
            if (device != cachedDevice)
            {
                // Looked up once per device switch, not per frame.
                cachedDevice = device;
                cachedPosition = device.TryGetChildControl<Vector3Control>(useController ? "pointerPosition" : "devicePosition");
                cachedRotation = device.TryGetChildControl<QuaternionControl>(useController ? "pointerRotation" : "deviceRotation");
            }

            if (cachedPosition == null || cachedRotation == null)
            {
                return;
            }

            Source = useController ? Modality.Controllers : Modality.Hands;
            HasPose = true;
            transform.SetLocalPositionAndRotation(cachedPosition.ReadValue(), cachedRotation.ReadValue());
        }

        // The first bound device whose isTracked button reads pressed right now.
        private static InputDevice TrackedDevice(InputAction tracked)
        {
            var controls = tracked.controls;
            for (var i = 0; i < controls.Count; i++)
            {
                if (controls[i] is ButtonControl button && button.isPressed)
                {
                    return button.device;
                }
            }

            return null;
        }
    }
}
