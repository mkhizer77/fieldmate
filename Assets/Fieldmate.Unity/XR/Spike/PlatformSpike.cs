using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.XR.Spike
{
    /// <summary>
    /// Week-1 platform spike (#3): proves passthrough, scene mesh, a persisted anchor, depth occlusion and one on-demand
    /// camera frame through AR Foundation + Unity OpenXR: Meta. Place the anchor where the right controller or right hand
    /// points (trigger / right index pinch), capture a camera frame (A / left index pinch), erase the saved anchor
    /// (B / right middle pinch). A head-locked panel and logcat ("[Spike]") report status.
    /// Throwaway: the reusable parts live in <see cref="QuestPermissions"/>, <see cref="CameraFrameCapture"/> and
    /// <see cref="AnchorIdCodec"/>.
    /// </summary>
    public sealed class PlatformSpike : MonoBehaviour
    {
        private const string AnchorPrefsKey = "fieldmate.spike.anchor";
        private const float MaxPointerDistance = 6f;
        private const float StatusInterval = 0.25f;
        // Hand-tracking pinches flicker around the press threshold; ignore repeats within this window.
        private const float ActionCooldown = 1.0f;

        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private ARShaderOcclusion shaderOcclusion;
        [SerializeField] private ARAnchorManager anchorManager;
        [SerializeField] private Transform head;
        [SerializeField] private Transform rightController;
        [SerializeField] private Material anchorMaterial;
        [SerializeField] private Material previewMaterial;
        [SerializeField] private Material pointerMaterial;

        private InputAction placeAction;
        private InputAction captureAction;
        private InputAction eraseAction;

        private LineRenderer pointer;
        private Text statusText;
        private Transform statusPanel;
        private Renderer previewQuad;
        private Texture2D lastCapture;

        private ARAnchor anchor;
        private bool anchorBusy;
        private bool sceneGranted;
        private bool cameraGranted;
        private string anchorStatus = "none";
        private string captureStatus = "none (press A)";
        private float cameraIndicatorUntil;
        private float nextStatusTime;
        private float smoothedFps = 72f;
        private int captureCount;
        private float nextActionTime;

        private void Awake()
        {
            // Scene-data managers stay off until USE_SCENE is granted (Meta OpenXR docs). The camera manager stays off until
            // HEADSET_CAMERA is answered: the provider checks the permission once when it starts, and restarting it later
            // stopped passthrough on device.
            meshManager.enabled = false;
            shaderOcclusion.enabled = false;
            occlusionManager.enabled = false;
            cameraManager.enabled = false;

            // Controllers and hands (Meta hand-tracking aim pinches) drive the same actions.
            placeAction = new InputAction("Place", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            placeAction.AddBinding("<MetaAimHand>{RightHand}/indexPressed");
            captureAction = new InputAction("Capture", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            captureAction.AddBinding("<MetaAimHand>{LeftHand}/indexPressed");
            eraseAction = new InputAction("Erase", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            eraseAction.AddBinding("<MetaAimHand>{RightHand}/middlePressed");

            BuildPointer();
            BuildStatusPanel();
            BuildPreviewQuad();
        }

        private void OnEnable()
        {
            placeAction.performed += OnPlace;
            captureAction.performed += OnCapture;
            eraseAction.performed += OnErase;
            placeAction.Enable();
            captureAction.Enable();
            eraseAction.Enable();
        }

        private void OnDisable()
        {
            placeAction.performed -= OnPlace;
            captureAction.performed -= OnCapture;
            eraseAction.performed -= OnErase;
            placeAction.Disable();
            captureAction.Disable();
            eraseAction.Disable();
        }

        private void OnDestroy()
        {
            placeAction.Dispose();
            captureAction.Dispose();
            eraseAction.Dispose();
            if (lastCapture != null)
            {
                Destroy(lastCapture);
            }
        }

        private void Start()
        {
            Log($"start: anchors save={anchorManager.descriptor?.supportsSaveAnchor} load={anchorManager.descriptor?.supportsLoadAnchor} erase={anchorManager.descriptor?.supportsEraseAnchor}");
            QuestPermissions.Request(new[] { QuestPermissions.Scene, QuestPermissions.HeadsetCamera }, OnPermissionResult);
            _ = LoadSavedAnchorAsync();
        }

        private void OnPermissionResult(string permission, bool granted, bool wasPrompted)
        {
            Log($"permission {permission}: {(granted ? "granted" : "DENIED")}{(wasPrompted ? " (prompted)" : string.Empty)}");
            if (permission == QuestPermissions.Scene)
            {
                sceneGranted = granted;
                meshManager.enabled = granted;
                occlusionManager.enabled = granted;
                shaderOcclusion.enabled = granted;
            }
            else if (permission == QuestPermissions.HeadsetCamera)
            {
                cameraGranted = granted;
                cameraManager.enabled = true; // passthrough starts either way; images only work if granted
            }
        }

        private void Update()
        {
            UpdatePointer();

            var dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                smoothedFps = Mathf.Lerp(smoothedFps, 1f / dt, 0.05f);
            }

            FollowHead(statusPanel, 1.1f, -0.3f, 0.35f);
            if (Time.unscaledTime >= nextStatusTime)
            {
                nextStatusTime = Time.unscaledTime + StatusInterval;
                statusText.text = BuildStatus();
            }
        }

        // ---------- anchors ----------

        private bool TryConsumeAction()
        {
            if (Time.unscaledTime < nextActionTime)
            {
                return false;
            }

            nextActionTime = Time.unscaledTime + ActionCooldown;
            return true;
        }

        private void OnPlace(InputAction.CallbackContext context)
        {
            if (!anchorBusy && TryConsumeAction())
            {
                _ = PlaceAnchorAsync(PointerHit(out var hitMesh), hitMesh);
            }
        }

        private void OnErase(InputAction.CallbackContext context)
        {
            if (!anchorBusy && TryConsumeAction())
            {
                _ = EraseAnchorAsync();
            }
        }

        private async Awaitable PlaceAnchorAsync(Vector3 position, bool onMesh)
        {
            anchorBusy = true;
            try
            {
                await EraseAnchorAsync(keepBusy: true);

                var facing = Vector3.ProjectOnPlane(head.position - position, Vector3.up);
                var rotation = facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;
                var added = await anchorManager.TryAddAnchorAsync(new Pose(position, rotation));
                if (!added.status.IsSuccess())
                {
                    anchorStatus = $"add FAILED ({added.status.statusCode})";
                    Log(anchorStatus);
                    return;
                }

                anchor = added.value;
                AttachVisual(anchor);
                anchorStatus = $"placed {(onMesh ? "on mesh" : "in air")}, saving...";

                var saved = await anchorManager.TrySaveAnchorAsync(anchor);
                if (saved.status.IsSuccess())
                {
                    PlayerPrefs.SetString(AnchorPrefsKey, AnchorIdCodec.Encode(saved.value));
                    PlayerPrefs.Save();
                    anchorStatus = $"placed + SAVED {Short(saved.value)} (restart app to test)";
                }
                else
                {
                    anchorStatus = $"placed, save FAILED ({saved.status.statusCode})";
                }

                Log($"anchor {anchorStatus} at {Format(position)}");
            }
            catch (Exception e)
            {
                anchorStatus = $"error: {e.Message}";
                Debug.LogException(e);
            }
            finally
            {
                anchorBusy = false;
            }
        }

        private async Awaitable LoadSavedAnchorAsync()
        {
            if (!AnchorIdCodec.TryDecode(PlayerPrefs.GetString(AnchorPrefsKey, string.Empty), out var id))
            {
                anchorStatus = "none saved (pull trigger to place)";
                return;
            }

            anchorBusy = true;
            try
            {
                anchorStatus = $"loading {Short(id)}...";
                var loaded = await anchorManager.TryLoadAnchorAsync(id);
                if (loaded.status.IsSuccess())
                {
                    anchor = loaded.value;
                    AttachVisual(anchor);
                    anchorStatus = $"LOADED {Short(id)} from previous session";
                }
                else
                {
                    anchorStatus = $"load FAILED ({loaded.status.statusCode}) for {Short(id)}";
                }

                Log($"anchor {anchorStatus}");
            }
            catch (Exception e)
            {
                anchorStatus = $"error: {e.Message}";
                Debug.LogException(e);
            }
            finally
            {
                anchorBusy = false;
            }
        }

        private async Awaitable EraseAnchorAsync(bool keepBusy = false)
        {
            anchorBusy = true;
            try
            {
                if (AnchorIdCodec.TryDecode(PlayerPrefs.GetString(AnchorPrefsKey, string.Empty), out var id))
                {
                    var erased = await anchorManager.TryEraseAnchorAsync(id);
                    Log($"erase {Short(id)}: {erased.statusCode}");
                    PlayerPrefs.DeleteKey(AnchorPrefsKey);
                    PlayerPrefs.Save();
                }

                if (anchor != null)
                {
                    anchorManager.TryRemoveAnchor(anchor);
                    anchor = null;
                }

                anchorStatus = "erased (pull trigger to place)";
            }
            finally
            {
                anchorBusy = keepBusy;
            }
        }

        private void AttachVisual(ARAnchor target)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "AnchorCube";
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(target.transform, false);
            cube.transform.localScale = Vector3.one * 0.2f;
            cube.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            cube.GetComponent<Renderer>().sharedMaterial = anchorMaterial;
        }

        private Vector3 PointerHit(out bool hitMesh)
        {
            var ray = new Ray(rightController.position, rightController.forward);
            hitMesh = Physics.Raycast(ray, out var hit, MaxPointerDistance);
            return hitMesh ? hit.point : ray.GetPoint(1.5f);
        }

        // ---------- camera frame ----------

        private void OnCapture(InputAction.CallbackContext context)
        {
            if (!TryConsumeAction())
            {
                return;
            }

            cameraIndicatorUntil = Time.unscaledTime + 1.5f;
            if (!CameraFrameCapture.TryCapture(cameraManager, head, out var frame, out var error))
            {
                captureStatus = $"FAILED: {error}";
                Log($"capture {captureStatus}");
                return;
            }

            if (lastCapture != null)
            {
                Destroy(lastCapture);
            }

            lastCapture = frame.Texture;
            captureCount++;
            ShowPreview(frame.Texture);

            var basePath = Path.Combine(Application.persistentDataPath, $"spike-capture-{captureCount}");
            File.WriteAllBytes(basePath + ".png", frame.Texture.EncodeToPNG());
            File.WriteAllText(basePath + ".json", CaptureJson(frame));

            var intrinsics = frame.HasIntrinsics
                ? $"f=({frame.Intrinsics.focalLength.x:F1},{frame.Intrinsics.focalLength.y:F1}) c=({frame.Intrinsics.principalPoint.x:F1},{frame.Intrinsics.principalPoint.y:F1}) res={frame.Intrinsics.resolution}"
                : "no intrinsics";
            captureStatus = $"#{captureCount} {frame.Texture.width}x{frame.Texture.height} {intrinsics}";
            Log($"capture {captureStatus} t={frame.TimestampSeconds:F3} head={Format(frame.HeadPose.position)} -> {basePath}.png");
        }

        private void ShowPreview(Texture2D texture)
        {
            previewMaterial.mainTexture = texture;
            var aspect = texture.height > 0 ? (float)texture.width / texture.height : 4f / 3f;
            previewQuad.transform.localScale = new Vector3(0.4f * aspect, 0.4f, 1f);
            previewQuad.gameObject.SetActive(true);
            FollowHead(previewQuad.transform, 0.9f, 0.15f, 1f);
        }

        private static string CaptureJson(in CapturedFrame frame)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append('{');
            sb.AppendFormat(ic, "\"width\":{0},\"height\":{1},\"timestamp\":{2:R},\"hasIntrinsics\":{3},",
                frame.Texture.width, frame.Texture.height, frame.TimestampSeconds, frame.HasIntrinsics ? "true" : "false");
            sb.AppendFormat(ic, "\"focalLength\":[{0:R},{1:R}],\"principalPoint\":[{2:R},{3:R}],\"resolution\":[{4},{5}],",
                frame.Intrinsics.focalLength.x, frame.Intrinsics.focalLength.y, frame.Intrinsics.principalPoint.x,
                frame.Intrinsics.principalPoint.y, frame.Intrinsics.resolution.x, frame.Intrinsics.resolution.y);
            var p = frame.HeadPose.position;
            var r = frame.HeadPose.rotation;
            sb.AppendFormat(ic, "\"headPosition\":[{0:R},{1:R},{2:R}],\"headRotation\":[{3:R},{4:R},{5:R},{6:R}]",
                p.x, p.y, p.z, r.x, r.y, r.z, r.w);
            sb.Append('}');
            return sb.ToString();
        }

        // ---------- status and visuals ----------

        private string BuildStatus()
        {
            var sb = new StringBuilder(512);
            if (Time.unscaledTime < cameraIndicatorUntil)
            {
                sb.AppendLine("<color=#ff4040>● CAMERA FRAME CAPTURED</color>");
            }

            sb.AppendLine("<b>Fieldmate platform spike</b>");
            sb.AppendLine("Anchor: trigger / R index pinch · Capture: A / L index pinch · Erase: B / R middle pinch");
            sb.AppendLine($"Session: {ARSession.state}   {smoothedFps:F0} fps");
            sb.AppendLine($"Permissions: scene {Flag(sceneGranted)} · camera {Flag(cameraGranted)}");
            sb.AppendLine($"Passthrough: {(cameraManager.enabled && cameraManager.subsystem is { running: true } ? "on" : "off")}");
            sb.AppendLine($"Scene mesh: {(meshManager.enabled ? meshManager.meshes.Count.ToString(CultureInfo.InvariantCulture) + " chunks" : "off")}");
            sb.AppendLine($"Occlusion: {(occlusionManager.enabled ? occlusionManager.currentEnvironmentDepthMode.ToString() : "off")}");
            sb.AppendLine($"Anchor: {anchorStatus}");
            sb.Append($"Capture: {captureStatus}");
            return sb.ToString();
        }

        private void FollowHead(Transform target, float distance, float height, float smoothing)
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            var goal = head.position + forward * distance + Vector3.up * height;
            target.position = Vector3.Lerp(target.position, goal, smoothing);
            target.rotation = Quaternion.Slerp(target.rotation, Quaternion.LookRotation(forward, Vector3.up), smoothing);
        }

        private void BuildPointer()
        {
            pointer = rightController.gameObject.AddComponent<LineRenderer>();
            pointer.useWorldSpace = true;
            pointer.positionCount = 2;
            pointer.widthMultiplier = 0.004f;
            pointer.sharedMaterial = pointerMaterial;
        }

        private void UpdatePointer()
        {
            var end = PointerHit(out _);
            pointer.SetPosition(0, rightController.position);
            pointer.SetPosition(1, end);
        }

        private void BuildStatusPanel()
        {
            var canvasGo = new GameObject("SpikeStatus", typeof(Canvas), typeof(Image));
            statusPanel = canvasGo.transform;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head.GetComponent<Camera>();
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(760f, 300f);
            rect.localScale = Vector3.one * 0.001f;
            canvasGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 12f);
            textRect.offsetMax = new Vector2(-16f, -12f);
            statusText = textGo.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 24;
            statusText.supportRichText = true;
            statusText.color = Color.white;
            statusText.alignment = TextAnchor.UpperLeft;
        }

        private void BuildPreviewQuad()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CapturePreview";
            Destroy(quad.GetComponent<Collider>());
            previewQuad = quad.GetComponent<Renderer>();
            previewQuad.sharedMaterial = previewMaterial;
            quad.SetActive(false);
        }

        private static string Flag(bool value) => value ? "yes" : "NO";

        private static string Short(SerializableGuid id) => AnchorIdCodec.Encode(id)[..8];

        private static string Format(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);

        private static void Log(string message) => Debug.Log($"[Spike] {message}");
    }
}
