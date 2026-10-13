using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;

namespace Fieldmate.XR
{
    /// <summary>
    /// Headset-side performance settings (#29), applied once XR is running and again after the app regains focus:
    /// fixed foveated rendering (fewer shaded pixels towards the lens edges, where the eye can't resolve them anyway)
    /// and CPU/GPU level hints, so the runtime holds sustained-high clocks instead of scaling down mid-procedure.
    /// Logs what took: "[Perf] foveation 1.00 (supported), levels cpu=SustainedHigh gpu=SustainedHigh (accepted)".
    /// </summary>
    public sealed class QuestPerformance : MonoBehaviour
    {
        private static readonly List<XRDisplaySubsystem> Displays = new();

        [Tooltip("0 off … 1 strongest. Quest maps it to its discrete levels (0.5 ≈ medium, 1 = high).")]
        [SerializeField, Range(0f, 1f)] private float foveation = 1f;

        [SerializeField] private PerformanceLevelHint cpuLevel = PerformanceLevelHint.SustainedHigh;
        [SerializeField] private PerformanceLevelHint gpuLevel = PerformanceLevelHint.SustainedHigh;

        private bool applied;
        private float nextTry;

        public float Foveation => foveation;

        /// <summary>True once settings reached a running XR display (never in the editor without a headset).</summary>
        public bool Applied => applied;

        private void Update()
        {
            // Check once a second until the display is up; then nothing per frame.
            if (applied || Time.unscaledTime < nextTry)
            {
                return;
            }

            nextTry = Time.unscaledTime + 1f;
            Apply();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                applied = false; // the runtime may reset levels while the app is in the background
            }
        }

        private void Apply()
        {
            SubsystemManager.GetSubsystems(Displays);
            XRDisplaySubsystem display = null;
            foreach (var candidate in Displays)
            {
                if (candidate.running)
                {
                    display = candidate;
                    break;
                }
            }

            if (display == null)
            {
                return;
            }

            display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None; // Quest 3: no eye tracking
            display.foveatedRenderingLevel = foveation;
            var cpu = XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Cpu, cpuLevel);
            var gpu = XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Gpu, gpuLevel);
            applied = true;
            Debug.Log($"[Perf] foveation {display.foveatedRenderingLevel:0.00} ({(display.foveatedRenderingLevel > 0f ? "on" : "unsupported")}), " +
                      $"levels cpu={cpuLevel} gpu={gpuLevel} ({(cpu && gpu ? "accepted" : "not supported")})");
        }
    }
}
