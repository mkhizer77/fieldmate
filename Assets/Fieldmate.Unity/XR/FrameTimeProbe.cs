using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>
    /// Logs frame timing every few seconds with the occlusion mode, for docs/perf (#6, #18, #29):
    /// "[Perf] occlusion=Hard main 6.1 ms render 4.3 ms gpu 9.1 ms cpu 13.9 ms frame 13.9 ms (360 frames)".
    /// <c>main</c> is the main thread's work without its wait for present, <c>render</c> the render thread's; together
    /// with <c>gpu</c> they are the numbers to fit in the 11 ms budget. <c>cpu</c> (the whole CPU frame, waits included)
    /// pins at the display interval whenever the app keeps up, which is what the first captures showed (13.9 ms).
    /// Uses FrameTimingManager ("Frame Timing Stats" in Player settings); values read 0 where the platform doesn't
    /// report them. No allocations.
    /// </summary>
    public sealed class FrameTimeProbe : MonoBehaviour
    {
        [SerializeField] private OcclusionSettings occlusion;
        [SerializeField] private float windowSeconds = 5f;

        private readonly FrameTiming[] timings = new FrameTiming[1];
        private double cpu;
        private double main;
        private double render;
        private double gpu;
        private double frame;
        private int frames;
        private float windowStart;

        /// <summary>Averages of the last logged window (ms); 0 until the first window closes.</summary>
        public float LastCpuMs { get; private set; }

        /// <summary>Main-thread work without the wait for present (ms).</summary>
        public float LastMainMs { get; private set; }

        public float LastRenderMs { get; private set; }
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
                main += System.Math.Max(0.0, timings[0].cpuMainThreadFrameTime - timings[0].cpuMainThreadPresentWaitTime);
                render += timings[0].cpuRenderThreadFrameTime;
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
            LastMainMs = (float)(main / frames);
            LastRenderMs = (float)(render / frames);
            LastGpuMs = (float)(gpu / frames);
            LastFrameMs = (float)(frame / frames);
            Debug.Log($"[Perf] occlusion={mode} main {LastMainMs:0.0} ms render {LastRenderMs:0.0} ms gpu {gpu / frames:0.0} ms cpu {cpu / frames:0.0} ms frame {frame / frames:0.0} ms ({frames} frames)");
            cpu = main = render = gpu = frame = 0;
            frames = 0;
            windowStart = Time.unscaledTime;
        }
    }
}
