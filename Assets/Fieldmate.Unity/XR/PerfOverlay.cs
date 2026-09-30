using Fieldmate.Procedures;
using Fieldmate.UI;
using TMPro;
using Unity.Profiling;
using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>
    /// In-app performance overlay (#18): frame time from <see cref="FrameTimeProbe"/>, draw calls, batches, SetPass calls
    /// and triangles from the render profiler counters, system and GC memory from the memory counters. Toggled by the
    /// Stats button on the machine; refreshed twice a second while shown; logs a "[Perf] stats" line every 5 s whether
    /// shown or not, so a capture needs only logcat. The counters cost nothing measurable; no per-frame allocations.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f;
        private const float LogSeconds = 5f;
        private const float WidthMm = 330f;
        private const float HeightMm = 150f;

        [SerializeField] private FrameTimeProbe probe;
        [SerializeField] private PressButton button;
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset;
        [SerializeField] private Transform head;

        private ProfilerRecorder drawCalls;
        private ProfilerRecorder batches;
        private ProfilerRecorder setPass;
        private ProfilerRecorder triangles;
        private ProfilerRecorder systemMemory;
        private ProfilerRecorder gcReserved;
        private TMP_Text text;
        private CanvasGroup group;
        private float nextRefresh;
        private float nextLog;

        public bool IsShown { get; private set; }
        public string Text => text != null ? text.text : string.Empty;
        public PerfStats Last { get; private set; }

        public void Configure(FrameTimeProbe frameProbe, PressButton statsButton, Transform machine, Vector3 localOffset, Transform headTransform)
        {
            probe = frameProbe;
            button = statsButton;
            anchor = machine;
            anchorOffset = localOffset;
            head = headTransform;
        }

        private void Awake()
        {
            Build();
            Apply(false);
        }

        private void OnEnable()
        {
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            if (button != null)
            {
                button.Pressed += Toggle;
            }
        }

        private void OnDisable()
        {
            drawCalls.Dispose();
            batches.Dispose();
            setPass.Dispose();
            triangles.Dispose();
            systemMemory.Dispose();
            gcReserved.Dispose();
            if (button != null)
            {
                button.Pressed -= Toggle;
            }
        }

        public void Toggle() => Apply(!IsShown);

        /// <summary>The current numbers (also used by tests).</summary>
        public PerfStats Sample() => new(
            probe != null ? probe.LastFrameMs : Time.unscaledDeltaTime * 1000f,
            probe != null ? probe.LastCpuMs : 0f,
            probe != null ? probe.LastGpuMs : 0f,
            Value(drawCalls), Value(batches), Value(setPass), Value(triangles), Value(systemMemory), Value(gcReserved));

        private static long Value(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue : 0L;

        private void Update()
        {
            var now = Time.unscaledTime;
            if (now >= nextLog)
            {
                nextLog = now + LogSeconds;
                Last = Sample();
                Debug.Log(Last.LogLine());
            }

            if (IsShown && now >= nextRefresh)
            {
                nextRefresh = now + RefreshSeconds;
                Last = Sample();
                text.text = Last.Overlay();
            }
        }

        private void LateUpdate()
        {
            if (!IsShown)
            {
                return;
            }

            if (anchor != null)
            {
                transform.position = anchor.TransformPoint(anchorOffset);
            }

            if (head != null)
            {
                UiKit.FaceAway(transform, head.position);
            }
        }

        private void Apply(bool shown)
        {
            IsShown = shown;
            group.alpha = shown ? 1f : 0f;
            if (button != null)
            {
                button.SetLabel(shown ? "Stats: On" : "Stats");
            }

            if (shown)
            {
                nextRefresh = 0f;
            }
        }

        private void Build()
        {
            UiKit.WorldCanvas(gameObject, WidthMm, HeightMm);
            group = GetComponent<CanvasGroup>();
            var card = UiKit.Card("Background", transform, Vector2.zero, Vector2.one, Theme.Surface, stroke: true).transform;
            var eyebrow = UiKit.Eyebrow("Eyebrow", card, new Vector2(0f, 0.72f), new Vector2(1f, 0.94f), Theme.TextMuted);
            eyebrow.rectTransform.offsetMin = new Vector2(Theme.Gap + 8f, 0f);
            eyebrow.text = "Performance · budget 11 ms / 120 draws";
            text = UiKit.Label("Stats", card, new Vector2(0f, 0.06f), new Vector2(1f, 0.72f), Theme.Caption, Theme.TextPrimary,
                TextAlignmentOptions.TopLeft, semiBold: true, new Vector2(Theme.Gap + 8f, 0f), new Vector2(-Theme.Gap, 0f));
            text.lineSpacing = 2f;
            text.text = "…";
        }
    }
}
