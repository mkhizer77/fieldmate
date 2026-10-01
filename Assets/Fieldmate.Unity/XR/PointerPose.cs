using UnityEngine;
using UnityEngine.InputSystem;

namespace Fieldmate.XR
{
    /// <summary>
    /// Drives a pointer from the controller's aim pose or the hand's aim pose, whichever the user is using
    /// (<see cref="InputModalityProbe"/>), never a mix. One action bound to both picked whichever control moved more, so
    /// with controllers in hand a still-reporting hand pose could steer the ray (device test 2026-10-01: controller
    /// placement pointed the wrong way). Falls back to the other source when the chosen one isn't tracked. Updates before
    /// the interactors and again just before rendering, like a TrackedPoseDriver.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public sealed class PointerPose : MonoBehaviour
    {
        [SerializeField] private string side = "Right";

        private InputAction controllerPosition;
        private InputAction controllerRotation;
        private InputAction controllerTracked;
        private InputAction handPosition;
        private InputAction handRotation;
        private InputAction handTracked;

        /// <summary>Which source drove the pointer last frame (tests, logs).</summary>
        public Modality Source { get; private set; }

        public void Configure(string hand) => side = hand;

        private void Awake()
        {
            controllerPosition = Value("Controller position", $"<XRController>{{{side}Hand}}/pointerPosition", "Vector3");
            controllerRotation = Value("Controller rotation", $"<XRController>{{{side}Hand}}/pointerRotation", "Quaternion");
            controllerTracked = Value("Controller tracked", $"<XRController>{{{side}Hand}}/isTracked", "Button");
            handPosition = Value("Hand position", $"<MetaAimHand>{{{side}Hand}}/devicePosition", "Vector3");
            handRotation = Value("Hand rotation", $"<MetaAimHand>{{{side}Hand}}/deviceRotation", "Quaternion");
            handTracked = Value("Hand tracked", $"<MetaAimHand>{{{side}Hand}}/isTracked", "Button");
        }

        private static InputAction Value(string name, string binding, string type) =>
            new(name, InputActionType.PassThrough, binding, expectedControlType: type);

        private void OnEnable()
        {
            controllerPosition.Enable();
            controllerRotation.Enable();
            controllerTracked.Enable();
            handPosition.Enable();
            handRotation.Enable();
            handTracked.Enable();
            Application.onBeforeRender += Apply;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= Apply;
            controllerPosition.Disable();
            controllerRotation.Disable();
            controllerTracked.Disable();
            handPosition.Disable();
            handRotation.Disable();
            handTracked.Disable();
        }

        private void OnDestroy()
        {
            controllerPosition.Dispose();
            controllerRotation.Dispose();
            controllerTracked.Dispose();
            handPosition.Dispose();
            handRotation.Dispose();
            handTracked.Dispose();
        }

        private void Update() => Apply();

        /// <summary>The active input's source if it is tracked, else whichever is.</summary>
        public static bool UseController(Modality active, bool controllerTracked, bool handTracked) =>
            active == Modality.Controllers ? controllerTracked || !handTracked : controllerTracked && !handTracked;

        private void Apply()
        {
            var controller = controllerTracked.ReadValue<float>() > 0.5f;
            var hand = handTracked.ReadValue<float>() > 0.5f;
            if (!controller && !hand)
            {
                return; // nothing tracked: keep the last pose
            }

            var useController = UseController(InputModalityProbe.Current, controller, hand);

            Source = useController ? Modality.Controllers : Modality.Hands;
            transform.SetLocalPositionAndRotation(
                (useController ? controllerPosition : handPosition).ReadValue<Vector3>(),
                (useController ? controllerRotation : handRotation).ReadValue<Quaternion>());
        }
    }
}
