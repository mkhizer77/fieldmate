using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Meshing;

namespace Fieldmate.XR
{
    /// <summary>
    /// The user's own hand shape, the way Horizon Home draws it: the runtime's fitted hand mesh (OpenXR
    /// XR_FB_hand_tracking_mesh via the Meta Quest: Hand Mesh Data feature) skinned once to 26 joint transforms that
    /// follow the tracked skeleton every frame, drawn with the light-yellow presence glow. Where the runtime has no
    /// mesh (editor, other devices) the fallback model stays on. A hand the runtime poses from a held controller is not
    /// drawn (#86): that pose is a fixed grip welded to the controller (measured on device 2026-10-02), never the user's
    /// real fingers, so the controller model is shown on its own. No per-frame allocations after the build.
    /// </summary>
    [RequireComponent(typeof(XRHandTrackingEvents))]
    public sealed class TrackedHandMesh : MonoBehaviour
    {
        private static readonly int JointCount = XRHandJointID.EndMarker.ToIndex();

        [SerializeField] private Material material;
        [SerializeField] private GameObject fallback;
        [Tooltip("The OpenXR plugin hands over the joints' bind transforms; Mesh.bindposes needs their inverses (verified on device 2026-09-30).")]
        [SerializeField] private bool invertBindPoses = true;

        private XRHandTrackingEvents events;
        private SkinnedMeshRenderer skinned;
        private Transform[] bones;
        private Mesh mesh;
        private bool queried;

        private bool holding;

        public bool IsBuilt => skinned != null;

        /// <summary>Whether a hand is drawn: tracked, and not posed from a controller it holds (#86).</summary>
        public static bool Shows(bool tracked, bool posedFromController) => tracked && !posedFromController;
        public SkinnedMeshRenderer Renderer => skinned;
        public int BoneCount => bones != null ? bones.Length : 0;

        public void Configure(Material glowMaterial, GameObject fallbackVisual)
        {
            material = glowMaterial;
            fallback = fallbackVisual;
        }

        private void Awake() => events = GetComponent<XRHandTrackingEvents>();

        private void OnEnable()
        {
            events.jointsUpdated.AddListener(OnJointsUpdated);
            events.trackingChanged.AddListener(OnTrackingChanged);
        }

        private void OnDisable()
        {
            events.jointsUpdated.RemoveListener(OnJointsUpdated);
            events.trackingChanged.RemoveListener(OnTrackingChanged);
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private void OnTrackingChanged(bool tracked)
        {
            if (skinned != null)
            {
                skinned.enabled = Shows(tracked, holding);
            }
        }

        // The data source can change while tracking continues (picking up or putting down a controller).
        private void UpdateHolding()
        {
            var now = HandDataSource.IsFromController(events.handedness);
            if (now == holding)
            {
                return;
            }

            holding = now;
            if (skinned != null)
            {
                skinned.enabled = Shows(events.handIsTracked, holding);
            }
            else if (fallback != null)
            {
                fallback.SetActive(!holding);
            }
        }

        private void OnJointsUpdated(XRHandJointsUpdatedEventArgs args)
        {
            UpdateHolding();
            if (skinned == null)
            {
                TryBuildFromRuntime();
                if (skinned == null)
                {
                    return;
                }
            }

            // Joint poses are in XR-origin space; this object sits under the origin's camera offset, so local = origin.
            for (var i = 0; i < JointCount; i++)
            {
                if (args.hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out var pose))
                {
                    bones[i].SetLocalPositionAndRotation(pose.position, pose.rotation);
                }
            }
        }

        // One query: the fitted mesh and its bind poses don't change during a session.
        private void TryBuildFromRuntime()
        {
            if (queried || events.subsystem == null)
            {
                return;
            }

            queried = true;
            var query = new XRHandMeshDataQueryParams { allocator = Allocator.Temp };
            if (!events.subsystem.TryGetMeshData(out var result, ref query))
            {
                result.Dispose();
                Debug.Log("[Presence] no runtime hand mesh; keeping the fallback model");
                return;
            }

            try
            {
                var data = events.handedness == Handedness.Left ? result.leftHand : result.rightHand;
                if (!data.positions.IsCreated || data.positions.Length == 0 || !data.indices.IsCreated || !data.boneWeights.IsCreated)
                {
                    Debug.Log("[Presence] runtime hand mesh data incomplete; keeping the fallback model");
                    return;
                }

                Build(data.positions, data.normals, data.uvs, data.indices, data.bonesPerVertex, data.boneWeights, data.jointBindPoseMatricesRaw);
            }
            finally
            {
                result.Dispose();
            }
        }

        /// <summary>Builds the skinned hand from mesh data (the runtime's, or synthetic in tests) and hides the fallback.</summary>
        public void Build(NativeArray<Vector3> positions, NativeArray<Vector3> normals, NativeArray<Vector2> uvs, NativeArray<int> indices,
            NativeArray<byte> bonesPerVertex, NativeArray<BoneWeight1> boneWeights, NativeArray<Matrix4x4> bindPoses)
        {
            mesh = new Mesh { name = $"{events.handedness} hand (runtime)" };
            mesh.SetVertices(positions);
            if (normals.IsCreated && normals.Length == positions.Length)
            {
                mesh.SetNormals(normals);
            }

            if (uvs.IsCreated && uvs.Length == positions.Length)
            {
                mesh.SetUVs(0, uvs);
            }

            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetBoneWeights(bonesPerVertex, boneWeights);
            if (!normals.IsCreated || normals.Length != positions.Length)
            {
                mesh.RecalculateNormals();
            }

            var bind = new Matrix4x4[JointCount];
            for (var i = 0; i < JointCount; i++)
            {
                var m = bindPoses.IsCreated && i < bindPoses.Length ? bindPoses[i] : Matrix4x4.identity;
                bind[i] = invertBindPoses ? m.inverse : m;
            }

            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            bones = new Transform[JointCount];
            for (var i = 0; i < JointCount; i++)
            {
                var bone = new GameObject(XRHandJointIDUtility.FromIndex(i).ToString()).transform;
                bone.SetParent(transform, false);
                bones[i] = bone;
            }

            var visual = new GameObject("Runtime Hand Mesh");
            visual.transform.SetParent(transform, false);
            skinned = visual.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.bones = bones;
            skinned.rootBone = bones[XRHandJointID.Wrist.ToIndex()];
            skinned.sharedMaterial = material;
            skinned.updateWhenOffscreen = true;
            skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skinned.receiveShadows = false;
            skinned.enabled = Shows(events.handIsTracked, holding);
            if (fallback != null)
            {
                fallback.SetActive(false);
            }

            Debug.Log($"[Presence] runtime hand mesh for {events.handedness}: {positions.Length} vertices, {indices.Length / 3} triangles");
        }
    }
}
