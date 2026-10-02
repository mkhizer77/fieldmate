using UnityEngine;
using UnityEngine.InputSystem;

namespace Fieldmate.XR
{
    /// <summary>
    /// In-headset alignment of the controller models (#80): while on (hand menu → Fit controllers), the right thumbstick
    /// slides both models sideways and forward/back, the left thumbstick up/down and tilts them, all at 2 cm/s and
    /// 20°/s, so they can be laid exactly over the real controllers seen through passthrough. Saved on leaving, and logged
    /// so the values can become the default.
    /// </summary>
    public sealed class ControllerFit : MonoBehaviour
    {
        private const float MetresPerSecond = 0.02f;
        private const float DegreesPerSecond = 20f;
        private const float Dead = 0.3f;

        private InputAction rightStick;
        private InputAction leftStick;
        private float nextLog;

        public static ControllerFit Instance { get; private set; }

        public bool Active { get; private set; }

        private void Awake()
        {
            Instance = this;
            rightStick = new InputAction("Fit right", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
            leftStick = new InputAction("Fit left", InputActionType.Value, "<XRController>{LeftHand}/primary2DAxis");
        }

        private void OnDestroy()
        {
            rightStick.Dispose();
            leftStick.Dispose();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetActive(bool on)
        {
            if (Active == on)
            {
                return;
            }

            Active = on;
            if (on)
            {
                rightStick.Enable();
                leftStick.Enable();
            }
            else
            {
                rightStick.Disable();
                leftStick.Disable();
                ControllerModel.SaveFit();
                Debug.Log($"[Presence] controller fit saved: {Describe(ControllerModel.Fit)}");
            }
        }

        private void Update()
        {
            if (!Active)
            {
                return;
            }

            var r = rightStick.ReadValue<Vector2>();
            var l = leftStick.ReadValue<Vector2>();
            var fit = ControllerModel.Fit;
            var dt = Time.deltaTime;
            if (Mathf.Abs(r.x) > Dead) fit.x += r.x * MetresPerSecond * dt;
            if (Mathf.Abs(r.y) > Dead) fit.z += r.y * MetresPerSecond * dt;
            if (Mathf.Abs(l.y) > Dead) fit.y += l.y * MetresPerSecond * dt;
            if (Mathf.Abs(l.x) > Dead) fit.w += l.x * DegreesPerSecond * dt;
            ControllerModel.Fit = fit;
            if (Time.unscaledTime >= nextLog && (r.sqrMagnitude > Dead * Dead || l.sqrMagnitude > Dead * Dead))
            {
                nextLog = Time.unscaledTime + 0.5f;
                Debug.Log($"[Presence] controller fit {Describe(fit)}");
            }
        }

        public static string Describe(Vector4 fit) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "x {0:0.000} y {1:0.000} z {2:0.000} m, pitch {3:0.0}°",
                fit.x, fit.y, fit.z, fit.w);
    }
}
