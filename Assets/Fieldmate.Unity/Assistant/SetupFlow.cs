using System.Collections;
using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.XR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// The guided setup on every launch (#71), one stage at a time: the headset looks at the room (scan if needed), the
    /// hologram mate materialises in front of the user and introduces itself, the user points it to where it should
    /// stay and pinches, the mate asks for the machine and the machine appears for placement, then the mate briefs the
    /// job and the procedure starts. The machine is hidden (not deactivated) until its stage, and is always placed
    /// fresh. Lines are spoken through <see cref="VoiceLoop.Narrate"/> (caption only without a voice) and each stage
    /// waits for its line. <see cref="AutoRun"/> off (PlayMode tests) skips straight to the old behaviour.
    /// </summary>
    public sealed class SetupFlow : MonoBehaviour
    {
        /// <summary>Off in PlayMode tests that are not about the setup (set before the scene loads).</summary>
        public static bool AutoRun = true;

        private const float ConfirmCooldown = 0.8f;
        private const float SpeechStartTimeout = 4f;

        [SerializeField] private SceneScanBootstrap scan;
        [SerializeField] private MachinePlacement placement;
        [SerializeField] private HologramMate mate;
        [SerializeField] private AssistantPanel panel;
        [SerializeField] private VoiceLoop loop;
        [SerializeField] private ProcedureDirector director;
        [SerializeField] private MachineServices machine;
        [SerializeField] private Transform machineRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Transform pointer;
        [SerializeField] private Material guideMaterial;

        private readonly HiddenObjects hidden = new();
        private InputAction confirmAction;
        private LineRenderer line;
        private bool mateConfirmed;
        private float confirmAfter;
        private Vector3 mateTarget;

        public SetupStage Stage { get; private set; } = SetupStage.RoomScan;

        public bool MachineHidden => hidden.IsHidden;

        public void Configure(SceneScanBootstrap roomScan, MachinePlacement machinePlacement, HologramMate hologram, AssistantPanel caption,
            VoiceLoop voice, ProcedureDirector procedure, MachineServices services, Transform machineTransform, Transform headTransform,
            Transform pointerTransform, Material lineMaterial)
        {
            scan = roomScan;
            placement = machinePlacement;
            mate = hologram;
            panel = caption;
            loop = voice;
            director = procedure;
            machine = services;
            machineRoot = machineTransform;
            head = headTransform;
            pointer = pointerTransform;
            guideMaterial = lineMaterial;
        }

        private void Awake()
        {
            if (AutoRun && placement != null)
            {
                placement.HoldForSetup(); // before its Start: no restore, no placing until the machine's stage
            }

            confirmAction = new InputAction("Place mate", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            confirmAction.AddBinding("<MetaAimHand>{RightHand}/indexPressed");
            confirmAction.AddBinding("<Keyboard>/enter");
            confirmAction.performed += _ => ConfirmMate();
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = 0.004f;
            line.sharedMaterial = guideMaterial;
            line.startColor = line.endColor = UI.Theme.Accent;
            line.enabled = false;
        }

        private void OnEnable() => confirmAction.Enable();

        private void OnDisable() => confirmAction.Disable();

        private void OnDestroy() => confirmAction.Dispose();

        private void Start()
        {
            if (head != null)
            {
                // Without the setup (tests), off to the front-right; with it, the setup moves it in front.
                mate.Place(InFront(1.1f) + Flat(head.right) * 0.55f);
            }

            if (!AutoRun)
            {
                Stage = SetupStage.Done;
                return;
            }

            StartCoroutine(Run());
        }

        /// <summary>Puts the mate where it is being pointed (the confirm input, and tests).</summary>
        public void ConfirmMate()
        {
            if (Stage == SetupStage.PlaceMate && Time.time >= confirmAfter)
            {
                mateConfirmed = true;
            }
        }

        private IEnumerator Run()
        {
            // 1. Room scan. Nothing to see yet but the caption.
            Stage = SetupStage.RoomScan;
            hidden.Hide(machineRoot);
            mate.SetVisible(false, instant: true);
            panel.ShowSetup("Setup · 1 of 4 · Room", SetupScript.RoomScan);
            Debug.Log("[Setup] room scan");
            yield return new WaitUntil(() => scan == null || scan.RoomReady);

            // 2. Meet the mate, a metre in front, a little below the eyes.
            Stage = SetupStage.MeetMate;
            mate.Place(InFront(1.0f));
            mate.SetVisible(true);
            Debug.Log("[Setup] mate appears");
            yield return new WaitForSeconds(1.2f); // let it materialise before it speaks
            var modality = InputModalityProbe.Current;
            panel.ShowSetup("Setup · 2 of 4 · Your mate", "Point where you want Fieldmate to stay, then " + InputWords.Grab(modality) + ".");
            yield return Say(SetupScript.Intro(InputWords.PlaceMate(modality)));

            // 3. The user points the mate into place.
            Stage = SetupStage.PlaceMate;
            mateConfirmed = false;
            confirmAfter = Time.time + ConfirmCooldown;
            line.enabled = pointer != null;
            mateTarget = mate.transform.position;
            while (!mateConfirmed)
            {
                FollowPointer();
                yield return null;
            }

            line.enabled = false;
            mate.Place(mateTarget);
            Debug.Log($"[Setup] mate placed at {mateTarget}");

            // 4. The machine appears and is placed fresh.
            Stage = SetupStage.PlaceMachine;
            hidden.Show();
            placement.BeginPlacing();
            panel.ShowSetup("Setup · 3 of 4 · Machine", InputWords.Place(InputModalityProbe.Current));
            yield return Say(SetupScript.PlaceMachine(InputWords.PlaceMachineSpoken(InputModalityProbe.Current)), waitForEnd: false);
            yield return new WaitUntil(() => placement.State == PlacementState.Placed);
            Debug.Log("[Setup] machine placed");

            // 5. Briefing, then the procedure.
            Stage = SetupStage.Briefing;
            var procedure = machine.Runner.Definition;
            panel.ShowSetup("Setup · 4 of 4 · Today", procedure.Title);
            var first = StepInstructions.For(procedure.Steps[0], machine.Catalog, InputModalityProbe.Current);
            yield return Say(SetupScript.Briefing(procedure, first));

            Stage = SetupStage.Done;
            if (machine.Runner.State != RunnerState.Running)
            {
                director.StartProcedure();
            }

            Debug.Log("[Setup] done: procedure started");

            // Where the app's controls went (#73): a short tip, then the card goes.
            panel.ShowSetup("Menu", InputWords.Menu(InputModalityProbe.Current) + ": Start, Move machine, Occlusion, Stats, Labels.");
            yield return new WaitForSeconds(10f);
            panel.HideDetail();
        }

        // Speaks (or captions) a line; with waitForEnd, returns once it has been heard (or read).
        private IEnumerator Say(string text, bool waitForEnd = true)
        {
            var started = Time.time;
            loop.Narrate(text, interrupt: false);
            if (!waitForEnd)
            {
                yield break;
            }

            var spoke = false;
            while (Time.time - started < SpeechStartTimeout)
            {
                if (loop.IsSpeaking)
                {
                    spoke = true;
                    break;
                }

                yield return null;
            }

            if (spoke)
            {
                yield return new WaitUntil(() => !loop.IsSpeaking);
            }
            else
            {
                var read = SetupScript.ReadSeconds(text) - (Time.time - started);
                if (read > 0f)
                {
                    yield return new WaitForSeconds(read); // no voice: leave the caption up long enough to read
                }
            }
        }

        private void FollowPointer()
        {
            if (pointer == null)
            {
                return;
            }

            var ray = new Ray(pointer.position, pointer.forward);
            var hit = Physics.Raycast(ray, out var info, MatePlacementMath.MaxSurfaceDistance + 2f, ~0, QueryTriggerInteraction.Ignore);
            mateTarget = MatePlacementMath.Target(ray, hit, hit ? info.point : default, hit ? info.normal : default, head.position.y);
            var k = 1f - Mathf.Exp(-Time.deltaTime / 0.12f);
            mate.Place(Vector3.Lerp(mate.transform.position, mateTarget, k));
            line.SetPosition(0, pointer.position);
            line.SetPosition(1, mate.transform.position);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.right;
        }

        // A point in front of the user at their eye height minus a bit, so the mate's face is just below eye level.
        private Vector3 InFront(float distance)
        {
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            var p = head.position + forward.normalized * distance;
            p.y = Mathf.Max(0.5f, head.position.y - 0.15f - HologramMate.EyeHeight);
            return p;
        }
    }
}
