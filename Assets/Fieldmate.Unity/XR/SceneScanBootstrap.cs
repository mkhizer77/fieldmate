using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Fieldmate.XR
{
    /// <summary>
    /// Room scan at start (#57, part 1): once the scene permission is granted, if the room has no scene data after a short
    /// wait, ask the headset to run Space Setup (scene capture) before the machine is placed. The placement hint says so
    /// meanwhile. Requested at most once per launch; the placement works on the tracked floor if the user skips it.
    /// <see cref="RoomReady"/> ends the guided setup's first stage (#71): room data arrived, or there will be none.
    /// </summary>
    public sealed class SceneScanBootstrap : MonoBehaviour
    {
        [SerializeField] private ARSession session;
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private MachinePlacement placement;

        private float permittedAt = -1f;
        private bool requested;
        private bool scanning;
        private bool gaveUp;
        private float requestedAt;

        /// <summary>After asking for a scan, how long to wait for room data before carrying on without it.</summary>
        public const float ScanTimeoutSeconds = 45f;

        public bool Requested => requested;

        /// <summary>Room data exists, or the scan is unavailable, denied, skipped or timed out.</summary>
        public bool RoomReady => gaveUp || (meshManager != null && meshManager.meshes.Count > 0);

        /// <summary>Carry on without room data (tests, or the user declining the scan).</summary>
        public void Skip()
        {
            gaveUp = true;
            Debug.Log("[Scan] skipped");
        }

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
            else
            {
                gaveUp = true;
                Debug.Log("[Scan] scene permission denied: no room data");
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

            if (scanning && Time.unscaledTime - requestedAt > ScanTimeoutSeconds && !gaveUp)
            {
                gaveUp = true;
                Debug.Log("[Scan] no room data after the scan request; carrying on with the tracked floor");
            }

            if (requested || !SceneScan.ShouldRequest(meshes, Time.unscaledTime - permittedAt))
            {
                return;
            }

            requested = true;
            requestedAt = Time.unscaledTime;
            var subsystem = session != null ? session.subsystem as MetaOpenXRSessionSubsystem : null;
            if (subsystem != null && subsystem.TryRequestSceneCapture())
            {
                scanning = true;
                placement?.SetScanning(true);
                Debug.Log("[Scan] no room data: asked the headset to scan the room");
            }
            else
            {
                gaveUp = true;
                Debug.LogWarning("[Scan] scene capture unavailable; placing on the tracked floor");
            }
        }
    }

    /// <summary>The decision, kept pure for tests.</summary>
    public static class SceneScan
    {
        /// <summary>How long to wait for existing scene data before asking for a scan.</summary>
        public const float WaitSeconds = 10f; // an already-scanned room delivered its mesh 8.8 s after launch (device 2026-09-30)

        public static bool ShouldRequest(int meshChunks, float secondsSincePermission) =>
            meshChunks == 0 && secondsSincePermission >= WaitSeconds;
    }
}
