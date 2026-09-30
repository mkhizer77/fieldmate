using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>
    /// Logs average CPU and GPU frame time every few seconds with the occlusion mode, for docs/perf (#6, #18):
    /// "[Perf] occlusion=Hard cpu 7.9 ms gpu 9.1 ms frame 13.9 ms (360 frames)". Uses FrameTimingManager (enable
    /// "Frame Timing Stats" in Player settings); GPU time reads 0 where the platform doesn't report it. No allocations.
    /// </summary>
    public sealed class FrameTimeProbe : MonoBehaviour
    {
        [SerializeField] private OcclusionSettings occlusion;
        [SerializeField] private float windowSeconds = 5f;

        private readonly FrameTiming[] timings = new FrameTiming[1];
        private double cpu;
        private double gpu;
        private double frame;
        private int frames;
        private float windowStart;

        /// <summary>Averages of the last logged window (ms); 0 until the first window closes.</summary>
        public float LastCpuMs { get; private set; }
        public float LastGpuMs { get; private set; }
        public float LastFrameMs { get; private set; }
        public float LastFps => LastFrameMs > 0f ? 1000f / LastFrameMs : 0f;

        public void Configure(OcclusionSettings settings) => occlusion = settings;

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpu += timings[0].cpuFrameTime;
                gpu += timings[0].gpuFrameTime;
            }

            frame += Time.unscaledDeltaTime * 1000.0;
            frames++;
            if (Time.unscaledTime - windowStart < windowSeconds)
            {
                return;
            }

            var mode = occlusion != null ? occlusion.Mode.ToString() : "n/a";
            LastCpuMs = (float)(cpu / frames);
            LastGpuMs = (float)(gpu / frames);
            LastFrameMs = (float)(frame / frames);
            Debug.Log($"[Perf] occlusion={mode} cpu {cpu / frames:0.0} ms gpu {gpu / frames:0.0} ms frame {frame / frames:0.0} ms ({frames} frames)");
            cpu = gpu = frame = 0;
            frames = 0;
            windowStart = Time.unscaledTime;
        }
    }
}
