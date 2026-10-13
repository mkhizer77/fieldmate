using System;
using System.Diagnostics;
using Fieldmate.Vision;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Debug = UnityEngine.Debug;

namespace Fieldmate.XR
{
    /// <summary>One on-demand passthrough frame with what's needed to turn its pixels into world rays (design.md §5.5).</summary>
    public readonly struct CameraFrame
    {
        public CameraFrame(Texture2D texture, CameraIntrinsics intrinsics, Pose headPose, Pose cameraPose, double timestampSeconds, int sequence)
        {
            Texture = texture;
            Intrinsics = intrinsics;
            HeadPose = headPose;
            CameraPose = cameraPose;
            TimestampSeconds = timestampSeconds;
            Sequence = sequence;
        }

        /// <summary>Upright RGBA, long edge ≤ 640 px. Owned by the source and overwritten by the next capture.</summary>
        public Texture2D Texture { get; }

        /// <summary>At <see cref="Texture"/>'s size; invalid (<see cref="CameraIntrinsics.IsValid"/>) when unreported.</summary>
        public CameraIntrinsics Intrinsics { get; }

        public Pose HeadPose { get; }

        /// <summary>Head pose at acquisition plus the camera mount: the Meta OpenXR route has no camera extrinsics (ADR-001).</summary>
        public Pose CameraPose { get; }

        public double TimestampSeconds { get; }
        public int Sequence { get; }
    }

    /// <summary>
    /// The passthrough camera as a frame source (#20). Asks for HEADSET_CAMERA through <see cref="PermissionsBootstrap"/>
    /// and starts the camera manager once, after the answer: the provider checks the permission when it starts, and
    /// restarting it later blacked out passthrough on device (ADR-001). Captures only on request, at most once a second,
    /// into one reused texture, so repeated captures don't grow memory. Never call from Update.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public sealed class CameraFrameSource : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Transform head;

        [Tooltip("Head → left RGB camera, head space (m). Approximate; see CameraMount.Quest3Left.")]
        [SerializeField] private Vector3 mountOffset = CameraMount.Quest3Left.Offset;

        [Tooltip("Camera orientation relative to the head (degrees).")]
        [SerializeField] private Vector3 mountEuler;

        private Texture2D texture;
        private bool answered;
        private bool granted;
        private bool frameSeen;
        private double lastCapture = double.NegativeInfinity;
        private int sequence;

        /// <summary>Raised after every successful capture, for the privacy indicator (#23).</summary>
        public event Action<CameraFrame> Captured;

        public CameraState State =>
            !answered ? CameraState.WaitingForPermission
            : !granted ? CameraState.Denied
            : frameSeen ? CameraState.Ready
            : CameraState.Starting;

        public CameraMount Mount => new(mountOffset, Quaternion.Euler(mountEuler));

        public Transform Head => head;

        private void Awake()
        {
            if (cameraManager != null)
            {
                cameraManager.enabled = false; // the scene ships it off; this covers a scene that doesn't
            }
        }

        private void Start() => PermissionsBootstrap.WhenAnswered(QuestPermissions.HeadsetCamera, OnPermission);

        private void OnDestroy()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnFirstFrame;
            }

            if (texture != null)
            {
                Destroy(texture);
            }
        }

        private void OnPermission(bool isGranted)
        {
            answered = true;
            granted = isGranted;
            Debug.Log($"[Camera] HEADSET_CAMERA {(isGranted ? "granted" : "denied")}");
            if (cameraManager == null)
            {
                return;
            }

            // Passthrough needs the manager either way; CPU images only arrive when granted.
            cameraManager.frameReceived += OnFirstFrame;
            cameraManager.enabled = true;
        }

        private void OnFirstFrame(ARCameraFrameEventArgs args)
        {
            cameraManager.frameReceived -= OnFirstFrame;
            frameSeen = true;
            if (granted)
            {
                ChooseConfiguration();
            }
        }

        private void ChooseConfiguration()
        {
            try
            {
                using var configurations = cameraManager.GetConfigurations(Allocator.Temp);
                if (configurations.Length == 0)
                {
                    Debug.Log("[Camera] no configurations reported; keeping the default");
                    return;
                }

                var sizes = new (int, int)[configurations.Length];
                for (var i = 0; i < sizes.Length; i++)
                {
                    sizes[i] = (configurations[i].width, configurations[i].height);
                }

                var pick = configurations[CaptureSettings.PickConfiguration(sizes)];
                var current = cameraManager.currentConfiguration;
                Debug.Log($"[Camera] configurations {string.Join(", ", sizes)}; current {current?.resolution}, chosen {pick.resolution}");
                if (current != pick)
                {
                    cameraManager.currentConfiguration = pick;
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
                Debug.Log($"[Camera] configurations unavailable ({exception.Message}); keeping the default");
            }
        }

        /// <summary>
        /// Takes one frame now. False with a reason the assistant can pass on when the camera can't deliver: permission
        /// pending or denied, no image yet, or a capture less than a second ago.
        /// </summary>
        public bool TryCapture(out CameraFrame frame, out string error)
        {
            frame = default;
            var state = State;
            if (state is CameraState.WaitingForPermission or CameraState.Denied)
            {
                error = CaptureSettings.Explain(state);
                return false;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (!CaptureSettings.MayCapture(now, lastCapture))
            {
                error = "A frame was just taken; ask again in a moment.";
                return false;
            }

            if (cameraManager == null || !cameraManager.enabled || !cameraManager.TryAcquireLatestCpuImage(out var image))
            {
                error = CaptureSettings.Explain(CameraState.Starting);
                return false;
            }

            var timer = Stopwatch.StartNew();
            head.GetPositionAndRotation(out var headPosition, out var headRotation);
            var headPose = new Pose(headPosition, headRotation);
            CameraIntrinsics intrinsics;
            double timestamp;
            using (image)
            {
                var (width, height) = CaptureSettings.Fit(image.width, image.height);
                EnsureTexture(width, height);

                // Quest 3: MirrorX is upright (MirrorY came out rotated 180°, ADR-001). Downsampling happens in the
                // conversion itself (nearest neighbour), so no full-size copy is made.
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorX)
                {
                    outputDimensions = new Vector2Int(width, height),
                };
                image.Convert(conversion, texture.GetRawTextureData<byte>());
                texture.Apply(false);

                intrinsics = ReadIntrinsics(image.width, image.height).ScaledTo(width, height);
                timestamp = image.timestamp;
            }

            lastCapture = now;
            frame = new CameraFrame(texture, intrinsics, headPose, Mount.CameraPose(headPose), timestamp, ++sequence);
            Debug.Log($"[Camera] capture #{sequence} {texture.width}x{texture.height} intrinsics={(intrinsics.IsValid ? "yes" : "no")} in {timer.Elapsed.TotalMilliseconds:0.0} ms");
            Captured?.Invoke(frame);
            error = null;
            return true;
        }

        // Intrinsics at the camera image's size; some providers report them for the sensor's full resolution.
        private CameraIntrinsics ReadIntrinsics(int imageWidth, int imageHeight)
        {
            if (!cameraManager.TryGetIntrinsics(out var k) || k.focalLength.x <= 0f || k.focalLength.y <= 0f)
            {
                return default;
            }

            var resolution = k.resolution.x > 0 && k.resolution.y > 0 ? k.resolution : new Vector2Int(imageWidth, imageHeight);
            return new CameraIntrinsics(k.focalLength, k.principalPoint, resolution.x, resolution.y).ScaledTo(imageWidth, imageHeight);
        }

        private void EnsureTexture(int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height)
            {
                return;
            }

            if (texture != null)
            {
                Destroy(texture);
            }

            texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Camera Frame" };
        }

        internal void SetUpForTests(ARCameraManager manager, Transform headTransform)
        {
            cameraManager = manager;
            head = headTransform;
        }

        internal void SimulatePermissionForTests(bool isGranted) => OnPermission(isGranted);
    }
}
