using System;
using Fieldmate.Assistant;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.XR
{
    public enum PlacementState
    {
        Loading,
        Placing,
        Placed,
    }

    /// <summary>
    /// Places the machine on the real floor (design.md §3 step 1). On launch the saved anchor is restored; otherwise the
    /// machine follows the right controller / right-hand ray over the room-scan floor (or the tracked floor plane when
    /// there is no scan), facing the user. Trigger or right index pinch confirms and saves a persistent anchor; the
    /// thumbstick rotates; Y on the left controller re-places it.
    /// </summary>
    public sealed class MachinePlacement : MonoBehaviour
    {
        private const string AnchorKey = "fieldmate.machine.anchor";
        private const float ConfirmCooldown = 1f;
        private const float RotateDegreesPerSecond = 90f;

        [SerializeField] private Transform machine;
        [SerializeField] private Transform head;
        [SerializeField] private Transform pointer;
        [SerializeField] private ARAnchorManager anchorManager;
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private MachineServices services;
        [SerializeField] private Material pointerMaterial;

        private InputAction confirmAction;
        private InputAction moveAction;
        private InputAction rotateAction;
        private LineRenderer line;
        private Transform ring;
        private TextMesh hint;
        private ARAnchor anchor;
        private float extraYaw;
        private float nextConfirm;
        private bool busy;

        public PlacementState State { get; private set; } = PlacementState.Loading;

        /// <summary>Whether the last placement found real scene data (false: tracked-floor fallback).</summary>
        public bool LastHitWasSceneMesh { get; private set; }

        /// <summary>True when the current position came from the saved anchor at launch (not confirmed this session).</summary>
        public bool Restored { get; private set; }

        public event Action<PlacementState> StateChanged;

        private void Awake()
        {
            meshManager.enabled = false; // scene data needs USE_SCENE first
            confirmAction = new InputAction("Place", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            confirmAction.AddBinding("<MetaAimHand>{RightHand}/indexPressed");
            confirmAction.AddBinding("<Keyboard>/enter");
            moveAction = new InputAction("Move", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
            moveAction.AddBinding("<Keyboard>/m");
            rotateAction = new InputAction("Rotate", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
            confirmAction.performed += _ => OnConfirm();
            moveAction.performed += _ => BeginPlacing();
            BuildVisuals();
        }

        private void OnEnable()
        {
            InputModalityProbe.Changed += OnModalityChanged;
            confirmAction.Enable();
            moveAction.Enable();
            rotateAction.Enable();
        }

        private void OnDisable()
        {
            InputModalityProbe.Changed -= OnModalityChanged;
            confirmAction.Disable();
            moveAction.Disable();
            rotateAction.Disable();
        }

        private void OnDestroy()
        {
            confirmAction.Dispose();
            moveAction.Dispose();
            rotateAction.Dispose();
        }

        private async void Start()
        {
            PermissionsBootstrap.WhenAnswered(QuestPermissions.Scene, granted => meshManager.enabled = granted);
            await RestoreAsync();
        }

        private void Update()
        {
            if (State != PlacementState.Placing)
            {
                return;
            }

            var stick = rotateAction.ReadValue<Vector2>();
            if (Mathf.Abs(stick.x) > 0.3f)
            {
                extraYaw = PlacementMath.WrapYaw(extraYaw + stick.x * RotateDegreesPerSecond * Time.deltaTime);
            }

            if (TryGetTarget(out var position))
            {
                machine.SetPositionAndRotation(position, PlacementMath.FacingViewer(position, head.position, extraYaw));
                ShowGuide(position, true);
            }
            else
            {
                ShowGuide(pointer.position + pointer.forward * 1.5f, false);
            }
        }

        /// <summary>Starts (re)placement. The old anchor is erased when the new one is confirmed.</summary>
        public void BeginPlacing()
        {
            if (busy)
            {
                return;
            }

            SetState(PlacementState.Placing);
            nextConfirm = Time.unscaledTime + ConfirmCooldown;
        }

        /// <summary>Confirms the current pose (also used by tests).</summary>
        public async void OnConfirm()
        {
            if (State != PlacementState.Placing || busy || Time.unscaledTime < nextConfirm)
            {
                return;
            }

            nextConfirm = Time.unscaledTime + ConfirmCooldown;
            await ConfirmAsync(new Pose(machine.position, machine.rotation));
        }

        public async Awaitable ConfirmAsync(Pose pose)
        {
            busy = true;
            try
            {
                await EraseSavedAsync();
                ARAnchor placed = null;
                var persisted = false;
                try
                {
                    var added = await anchorManager.TryAddAnchorAsync(pose);
                    if (added.status.IsSuccess())
                    {
                        placed = added.value;
                        var saved = await anchorManager.TrySaveAnchorAsync(placed);
                        if (saved.status.IsSuccess())
                        {
                            PlayerPrefs.SetString(AnchorKey, AnchorIdCodec.Encode(saved.value));
                            PlayerPrefs.Save();
                            persisted = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    // No anchor subsystem (Editor) or tracking lost: fall through to a session-only placement.
                    Debug.LogWarning($"[Placement] anchor unavailable: {e.Message}");
                }

                if (placed != null)
                {
                    Attach(placed);
                }
                else
                {
                    machine.SetPositionAndRotation(pose.position, pose.rotation);
                }

                Debug.Log($"[Placement] placed at {pose.position}, anchored={placed != null}, saved={persisted}, sceneMesh={LastHitWasSceneMesh}");
                Restored = false;
                SetState(PlacementState.Placed);
                services.RefreshParts();
            }
            finally
            {
                busy = false;
            }
        }

        private async Awaitable RestoreAsync()
        {
            if (!AnchorIdCodec.TryDecode(PlayerPrefs.GetString(AnchorKey, string.Empty), out var id))
            {
                BeginPlacing();
                return;
            }

            busy = true;
            try
            {
                Result<ARAnchor> loaded;
                try
                {
                    loaded = await anchorManager.TryLoadAnchorAsync(id);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Placement] anchor loading unavailable: {e.Message}");
                    return;
                }

                if (loaded.status.IsSuccess())
                {
                    Attach(loaded.value);
                    Restored = true;
                    SetState(PlacementState.Placed);
                    Debug.Log("[Placement] restored saved machine anchor");
                    return;
                }

                Debug.LogWarning($"[Placement] saved anchor could not be loaded ({loaded.status.statusCode}); place the machine again");
            }
            finally
            {
                busy = false;
                if (State != PlacementState.Placed)
                {
                    BeginPlacing();
                }
            }
        }

        private async Awaitable EraseSavedAsync()
        {
            if (anchor != null)
            {
                machine.SetParent(null, true);
                anchorManager.TryRemoveAnchor(anchor);
                anchor = null;
            }

            if (AnchorIdCodec.TryDecode(PlayerPrefs.GetString(AnchorKey, string.Empty), out var id))
            {
                PlayerPrefs.DeleteKey(AnchorKey);
                try
                {
                    await anchorManager.TryEraseAnchorAsync(id);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Placement] could not erase the old anchor: {e.Message}");
                }
            }
        }

        private void Attach(ARAnchor target)
        {
            anchor = target;
            machine.SetParent(target.transform, false);
            machine.localPosition = Vector3.zero;
            machine.localRotation = Quaternion.identity;
        }

        private bool TryGetTarget(out Vector3 position)
        {
            var ray = new Ray(pointer.position, pointer.forward);
            // Room scan first: accept only floor-like hits, ignoring the machine's own colliders.
            var hits = Physics.RaycastAll(ray, PlacementMath.MaxDistance);
            var best = float.MaxValue;
            position = default;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(machine) || !PlacementMath.IsFloorLike(hit.normal) || hit.distance >= best)
                {
                    continue;
                }

                best = hit.distance;
                position = hit.point;
            }

            LastHitWasSceneMesh = best < float.MaxValue;
            return LastHitWasSceneMesh || PlacementMath.TryHitFloorPlane(ray, 0f, out position);
        }

        private void SetState(PlacementState state)
        {
            State = state;
            var placing = state == PlacementState.Placing;
            line.enabled = placing;
            ring.gameObject.SetActive(placing);
            hint.gameObject.SetActive(placing);
            StateChanged?.Invoke(state);
        }

        private void ShowGuide(Vector3 target, bool valid)
        {
            line.SetPosition(0, pointer.position);
            line.SetPosition(1, target);
            line.startColor = line.endColor = valid ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.4f, 0.3f);
            ring.position = target + Vector3.up * 0.005f;
            hint.transform.position = target + Vector3.up * 1.45f;
            hint.transform.rotation = Quaternion.LookRotation(hint.transform.position - head.position, Vector3.up);
        }

        private void OnModalityChanged(Modality modality) =>
            hint.text = $"{InputWords.Place(modality)}\n{InputWords.Move(modality)} to move it later";

        public string HintText => hint != null ? hint.text : string.Empty;

        private void BuildVisuals()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = 0.006f;
            line.sharedMaterial = pointerMaterial;
            line.enabled = false;

            ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            ring.name = "Placement ring";
            Destroy(ring.GetComponent<Collider>());
            ring.localScale = new Vector3(1.8f, 0.002f, 1.8f);
            ring.GetComponent<Renderer>().sharedMaterial = pointerMaterial;
            ring.gameObject.SetActive(false);

            hint = new GameObject("Placement hint").AddComponent<TextMesh>();
            hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hint.GetComponent<MeshRenderer>().sharedMaterial = hint.font.material;
            OnModalityChanged(InputModalityProbe.Current);
            hint.characterSize = 0.012f;
            hint.fontSize = 48;
            hint.anchor = TextAnchor.MiddleCenter;
            hint.alignment = TextAlignment.Center;
            hint.color = Color.white;
            hint.gameObject.SetActive(false);
        }
    }
}
