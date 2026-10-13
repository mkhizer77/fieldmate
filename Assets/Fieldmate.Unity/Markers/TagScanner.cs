using System.Threading.Tasks;
using Fieldmate.Assistant;
using Fieldmate.Localization;
using Fieldmate.Markers;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using Fieldmate.Vision;
using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>
    /// "Scan tag" (#27): one camera frame, a machine tag found on the headset (nothing is uploaded), its machine looked
    /// up by id through Addressables, and the machine anchored on the tag. An alternative to pointing at the floor that
    /// puts the twin exactly where the real machine's tag is. Every scan shows the camera indicator like any capture.
    /// </summary>
    public sealed class TagScanner : MonoBehaviour
    {
        public enum Outcome
        {
            Placed,
            CameraUnavailable,
            NoTag,
            UnknownMachine,
            OtherMachine,
            NoPose,
        }

        [SerializeField] private CameraFrameSource frameSource;
        [SerializeField] private MachineServices machine;
        [SerializeField] private MachinePlacement placement;
        [SerializeField] private VoiceLoop voice;
        [SerializeField] private AssistantPanel panel;
        [SerializeField] private PressButton button;

        private byte[] gray;
        private bool busy;

        /// <summary>The last scan's outcome and the sentence the user got.</summary>
        public (Outcome outcome, string message) Last { get; private set; }

        public void Configure(CameraFrameSource source, MachineServices services, MachinePlacement machinePlacement, VoiceLoop loop,
            AssistantPanel assistantPanel, PressButton scanButton)
        {
            frameSource = source;
            machine = services;
            placement = machinePlacement;
            voice = loop;
            panel = assistantPanel;
            button = scanButton;
        }

        private void Start()
        {
            if (button != null)
            {
                button.Pressed += OnPressed;
                button.SetLabel(Lang.T("Scan tag"));
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.Pressed -= OnPressed;
            }
        }

        private async void OnPressed() => await ScanAsync();

        public async Task<Outcome> ScanAsync()
        {
            if (busy)
            {
                return Last.outcome;
            }

            busy = true;
            button?.SetLabel(Lang.T("Scanning…"));
            try
            {
                var (outcome, message) = await RunAsync();
                Last = (outcome, message);
                Debug.Log($"[Tags] {outcome}: {message}");
                if (outcome == Outcome.Placed)
                {
                    voice?.RecordEvent("the machine was placed on its tag");
                    voice?.Narrate(message, interrupt: false);
                }
                else
                {
                    panel?.ShowBanner(message);
                }

                return outcome;
            }
            finally
            {
                busy = false;
                button?.SetLabel(Lang.T("Scan tag"));
            }
        }

        private async Task<(Outcome, string)> RunAsync()
        {
            if (!frameSource.TryCapture(out var frame, out var error))
            {
                return (Outcome.CameraUnavailable, error);
            }

            var texture = frame.Texture;
            gray = MarkerDetector.Luminance(texture.GetRawTextureData(), texture.width, texture.height, gray);
            var found = MarkerDetector.Detect(gray, texture.width, texture.height);
            if (found.Count == 0)
            {
                return (Outcome.NoTag, Lang.T("No machine tag in view. Look straight at the tag from about a metre."));
            }

            // The biggest tag in view is the one the user is looking at.
            var tag = found[0];
            foreach (var candidate in found)
            {
                if (Size(candidate) > Size(tag))
                {
                    tag = candidate;
                }
            }

            var definition = await MachineCatalog.LoadAsync(tag.Id);
            if (definition == null)
            {
                return (Outcome.UnknownMachine, Lang.F("Tag {0} isn't a machine this app knows.", tag.Id));
            }

            if (definition.MachineName != machine.Manual.MachineName)
            {
                return (Outcome.OtherMachine, Lang.F("This tag is for {0}; this app runs {1}.", definition.MachineName, machine.Manual.MachineName));
            }

            if (!MarkerPose.TryEstimate(tag.Corners, definition.TagSide, frame.Intrinsics, out var inCamera))
            {
                return (Outcome.NoPose, Lang.T("The camera can't measure the tag yet. Try again in a moment."));
            }

            var tagWorld = MarkerPose.ToWorld(inCamera, frame.CameraPose);
            if (definition.Mount == TagMountKind.Floor &&
                placement.TryFloorPoint(PixelToRay.FromPixel(tag.ProjectiveCenter, frame.Intrinsics, frame.CameraPose), out var onFloor))
            {
                // A small tag seen from standing height gives a good direction but a loose depth; the ray through its
                // centre meeting the real floor pins the position, the pose keeps the heading.
                tagWorld = new Pose(onFloor, tagWorld.rotation);
            }

            var pose = TagMount.MachinePose(tagWorld, definition.Mount, definition.Offset, definition.YawDegrees, PlacementMath.FloorClearance);
            Debug.Log($"[Tags] tag {tag.Id} ({tag.Errors} cells off) at {tagWorld.position}, machine at {pose.position}");
            await placement.ConfirmAsync(pose);
            return (Outcome.Placed, Lang.F("Found the {0} on its tag.", Lang.T(definition.SpokenName)));
        }

        private static float Size(MarkerDetection d) => (d.Corners[2] - d.Corners[0]).sqrMagnitude;
    }
}
