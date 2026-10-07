using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Knowledge;
using Fieldmate.Twin;
using Fieldmate.XR;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Fieldmate.Vision
{
    /// <summary>
    /// "What's this?" end to end (design.md §5.5, #22): one camera frame, the gaze ray and the twin's candidate parts go
    /// to the vision model; its JSON answer is fused with the twin and pinned as a label where the answer points. The
    /// frame is captured only here, on the user's request, and nothing is kept after the call.
    /// </summary>
    public sealed class VisionRequester : MonoBehaviour
    {
        private const float MaxRayMetres = 5f;
        private const float DefaultGazeMetres = 1.5f;

        [SerializeField] private CameraFrameSource frameSource;
        [SerializeField] private MachineServices machine;
        [SerializeField] private LabelAnchor labels;
        [SerializeField, Range(40, 95)] private int jpegQuality = 75;
        [SerializeField] private float timeoutSeconds = 6f;

        private readonly RaycastHit[] hits = new RaycastHit[16];
        private IVisionModel model;
        private bool busy;

        internal delegate bool CaptureFn(out CameraFrame frame, out string error);

        /// <summary>Tests stand in for the camera, which the editor doesn't have.</summary>
        internal CaptureFn CaptureForTests;

        /// <summary>Raised with every answer the user gets (also from model data), for tests and the debug overlay.</summary>
        public event Action<FusedAnswer> Answered;

        /// <summary>True once a vision model is attached; the assistant only offers identify_view then.</summary>
        public bool HasModel => model != null;

        /// <summary>The last request's breakdown, e.g. "capture 4 ms, model 1820 ms, total 1850 ms".</summary>
        public string LastTiming { get; private set; } = string.Empty;

        public LabelAnchor Labels => labels;

        public void SetModel(IVisionModel visionModel) => model = visionModel;

        public async Task<ViewAnswer> IdentifyAsync(string languageCode, CancellationToken cancellationToken)
        {
            if (busy)
            {
                return ViewAnswer.Unavailable("Still looking at the last frame.");
            }

            busy = true;
            try
            {
                return await RunAsync(languageCode, cancellationToken);
            }
            finally
            {
                busy = false;
            }
        }

        private async Task<ViewAnswer> RunAsync(string languageCode, CancellationToken cancellationToken)
        {
            var total = Stopwatch.StartNew();
            var catalog = machine.Catalog;
            var captured = CaptureForTests != null
                ? CaptureForTests(out var frame, out var cameraError)
                : frameSource.TryCapture(out frame, out cameraError);
            var headPose = captured ? frame.HeadPose : new Pose(frameSource.Head.position, frameSource.Head.rotation);
            var gaze = new Ray(headPose.position, headPose.rotation * Vector3.forward);
            var gazed = machine.PartAlong(gaze, out var gazeHit, MaxRayMetres);
            var gazeDistance = gazed != null ? Vector3.Distance(gaze.origin, gazeHit) : DefaultGazeMetres;
            var captureMs = total.Elapsed.TotalMilliseconds;

            VisionAnswer answer = null;
            var modelMs = 0d;
            if (captured && model != null)
            {
                var candidates = CandidateParts(gaze, gazed?.Id, catalog);
                Vector2? gazePoint = frame.Intrinsics.IsValid &&
                                     PixelToRay.TryGazePixel(gaze, gazeDistance, frame.Intrinsics, frame.CameraPose, out var pixel)
                    ? new Vector2(pixel.x / frame.Intrinsics.Width, pixel.y / frame.Intrinsics.Height)
                    : null;
                var prompt = VisionQuery.BuildPrompt(candidates, gazePoint, machine.Runner?.CurrentStep?.Title, languageCode);
                var jpeg = frame.Texture.EncodeToJPG(jpegQuality);

                var asked = Stopwatch.StartNew();
                answer = await AskAsync(jpeg, prompt, cancellationToken);
                modelMs = asked.Elapsed.TotalMilliseconds;
                Debug.Log($"[Vision] frame #{frame.Sequence} {jpeg.Length / 1024} KB, {candidates.Count} candidates, gaze {(gazePoint?.ToString("0.00") ?? "unknown")}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var fused = AnswerFusion.Fuse(answer, gazed?.Id, catalog);
            if (fused.Source != AnswerSource.None)
            {
                labels.Show(fused, AnchorPoint(fused, captured ? frame : default, gaze, gazed != null ? gazeHit : gaze.GetPoint(gazeDistance)));
                Answered?.Invoke(fused);
            }

            LastTiming = $"capture {captureMs:0} ms, model {modelMs:0} ms, total {total.Elapsed.TotalMilliseconds:0} ms";
            Debug.Log($"[Vision] {fused.Source} '{fused.Label}' ({fused.PartId ?? "-"}, {fused.Confidence:0.00}) {LastTiming}");

            if (!captured && fused.Source == AnswerSource.None)
            {
                return ViewAnswer.Unavailable(cameraError);
            }

            if (fused.Source == AnswerSource.None)
            {
                return ViewAnswer.Unavailable(fused.Describe());
            }

            // Without a frame the label still comes from the twin; say why the camera didn't help.
            return ViewAnswer.Of(captured ? fused.Describe() : $"{fused.Describe()} (Camera: {cameraError})");
        }

        private async Task<VisionAnswer> AskAsync(byte[] jpeg, string prompt, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                var text = await model.AskAsync(jpeg, "image/jpeg", prompt, timeout.Token);
                if (VisionQuery.TryParseAnswer(text, out var answer, out var error))
                {
                    return answer;
                }

                Debug.LogWarning($"[Vision] {error}: {text}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Debug.LogWarning($"[Vision] no answer within {timeoutSeconds:0} s");
            }
            catch (ProviderException e)
            {
                Debug.LogWarning($"[Vision] {e.ErrorType}: {e.Message}");
            }

            return null; // the twin answers instead
        }

        private List<PartInfo> CandidateParts(Ray gaze, string gazedId, PartCatalog catalog)
        {
            var points = new List<PartPoint>(machine.Parts.Count);
            foreach (var pair in machine.Parts)
            {
                if (pair.Value != null && TryBounds(pair.Value, out var bounds))
                {
                    // The part's point nearest the gaze, so a long part (the motor) counts when the gaze crosses its end.
                    var along = Mathf.Max(0f, Vector3.Dot(bounds.center - gaze.origin, gaze.direction));
                    points.Add(new PartPoint(pair.Key, bounds.ClosestPoint(gaze.GetPoint(along))));
                }
            }

            var parts = new List<PartInfo>();
            foreach (var id in VisionQuery.Candidates(points, gaze, gazedId))
            {
                if (catalog.TryGet(id, out var part))
                {
                    parts.Add(part);
                }
            }

            return parts;
        }

        // Box centre → camera ray → first real surface (twin part or room mesh); else the gaze point.
        private Vector3 AnchorPoint(FusedAnswer fused, CameraFrame frame, Ray gaze, Vector3 gazePoint)
        {
            if (fused.Box is not { } box || !frame.Intrinsics.IsValid)
            {
                return gazePoint;
            }

            var ray = PixelToRay.FromNormalized(box.Center, frame.Intrinsics, frame.CameraPose);
            var count = Physics.RaycastNonAlloc(ray, hits, MaxRayMetres, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var point = ray.GetPoint(Vector3.Distance(gaze.origin, gazePoint));
            for (var i = 0; i < count; i++)
            {
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    point = hits[i].point;
                }
            }

            return point;
        }

        private static bool TryBounds(PartTag part, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in part.GetComponentsInChildren<Renderer>())
            {
                if (renderer is not MeshRenderer and not SkinnedMeshRenderer)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        internal float TimeoutSeconds
        {
            get => timeoutSeconds;
            set => timeoutSeconds = value;
        }
    }
}
