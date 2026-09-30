using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Fieldmate.XR
{
    /// <summary>
    /// Room scan at start (#57, part 1): once the scene permission is granted, if the room has no scene data after a short
    /// wait, ask the headset to run Space Setup (scene capture) before the machine is placed. The placement hint says so
    /// meanwhile. Requested at most once per launch; the placement works on the tracked floor if the user skips it.
    /// </summary>
    public sealed class SceneScanBootstrap : MonoBehaviour
    {
        [SerializeField] private ARSession session;
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private MachinePlacement placement;

        private float permittedAt = -1f;
        private bool requested;
        private bool scanning;

        public bool Requested => requested;

        public void Configure(ARSession arSession, ARMeshManager meshes, MachinePlacement machinePlacement)
        {
            session = arSession;
            meshManager = meshes;
            placement = machinePlacement;
        }

        private void Start() => PermissionsBootstrap.WhenAnswered(QuestPermissions.Scene, granted =>
        {
            if (granted)
            {
                permittedAt = Time.unscaledTime;
            }
        });

        private void Update()
        {
            if (permittedAt < 0f)
            {
                return;
            }

            var meshes = meshManager != null ? meshManager.meshes.Count : 0;
            if (scanning && meshes > 0)
            {
                scanning = false;
                placement?.SetScanning(false);
                Debug.Log($"[Scan] room data available ({meshes} mesh chunks)");
                return;
            }

            if (requested || !SceneScan.ShouldRequest(meshes, Time.unscaledTime - permittedAt))
            {
                return;
            }

            requested = true;
            var subsystem = session != null ? session.subsystem as MetaOpenXRSessionSubsystem : null;
            if (subsystem != null && subsystem.TryRequestSceneCapture())
            {
                scanning = true;
                placement?.SetScanning(true);
                Debug.Log("[Scan] no room data: asked the headset to scan the room");
            }
            else
            {
                Debug.LogWarning("[Scan] scene capture unavailable; placing on the tracked floor");
            }
        }
    }

    /// <summary>The decision, kept pure for tests.</summary>
    public static class SceneScan
    {
        /// <summary>How long to wait for existing scene data before asking for a scan.</summary>
        public const float WaitSeconds = 4f;

        public static bool ShouldRequest(int meshChunks, float secondsSincePermission) =>
            meshChunks == 0 && secondsSincePermission >= WaitSeconds;
    }
}
