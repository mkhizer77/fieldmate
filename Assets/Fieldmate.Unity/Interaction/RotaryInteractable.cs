using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// A constrained rotary control for valves, levers and the breaker (design.md §5.2). Grab it (pinch or grip) and turn:
    /// the handle rotates about <see cref="axis"/> (in the handle's parent space), clamped to [min, max], with named detents
    /// that raise states for the procedure and haptic ticks along the way. With <see cref="requiredHands"/> = 2 it only
    /// turns while both hands hold it, following the line between them (like a steering wheel). On release it snaps to a
    /// nearby detent. No per-frame allocations.
    /// </summary>
    [RequireComponent(typeof(HoverTint))]
    public sealed class RotaryInteractable : XRBaseInteractable, IMachineControl
    {
        [SerializeField] private string partId;
        [SerializeField] private Transform handle;
        [SerializeField] private Vector3 axis = Vector3.up;
        [SerializeField] private float minAngle;
        [SerializeField] private float maxAngle = 90f;
        [SerializeField] private float startAngle;
        [SerializeField] private float[] detentAngles = { 0f, 90f };
        [SerializeField] private string[] detentStates = { "open", "closed" };
        [SerializeField] private float tickDegrees = 30f;
        [SerializeField, Range(1, 2)] private int requiredHands = 1;
        [SerializeField, Range(0.5f, 3f)] private float turnGain = 1f;

        private RotaryTracker tracker;
        private Detents detents;
        private Quaternion baseRotation;
        private AudioSource clickSource;
        private bool turning;

        public string PartId => partId;
        public string State => detents?.State;
        public float Normalized => tracker?.Normalized ?? 0f;
        public float Angle => tracker?.Angle ?? startAngle;
        public int RequiredHands => requiredHands;
        public float TurnGain => turnGain;
        public bool IsTurning => turning;
        public Vector3 Axis => axis;
        public Transform Handle => handle;
        public float MinAngle => minAngle;
        public float MaxAngle => maxAngle;

        /// <summary>The angle of a named position ("closed", "locked"...), for the step guide.</summary>
        public bool TryGetDetentAngle(string state, out float angle)
        {
            for (var i = 0; i < detentStates.Length; i++)
            {
                if (detentStates[i] == state)
                {
                    angle = detentAngles[i];
                    return true;
                }
            }

            angle = 0f;
            return false;
        }

        public event Action<string, string> StateReached;

        /// <summary>Sets up the control in code (the scene builder and tests); call before the first frame.</summary>
        public void Configure(string part, Transform turningHandle, Vector3 turnAxis, float min, float max, float start,
            float[] angles, string[] states, float tick, int hands, float gain = 1f)
        {
            turnGain = gain;
            partId = part;
            handle = turningHandle;
            axis = turnAxis;
            minAngle = min;
            maxAngle = max;
            startAngle = start;
            detentAngles = angles;
            detentStates = states;
            tickDegrees = tick;
            requiredHands = Mathf.Clamp(hands, 1, 2);
            Initialise();
        }

        protected override void Awake()
        {
            base.Awake();
            Initialise();
        }

        private void Initialise()
        {
            if (handle == null)
            {
                handle = transform;
            }

            selectMode = requiredHands > 1 ? InteractableSelectMode.Multiple : InteractableSelectMode.Single;
            baseRotation = handle.localRotation;
            tracker = new RotaryTracker(axis, minAngle, maxAngle, startAngle, turnGain);
            detents = new Detents(detentAngles, detentStates, tickDegrees, startAngle);
            if (clickSource == null)
            {
                clickSource = InteractionFeedback.CreateSource(gameObject);
            }
        }

        /// <summary>Turns to <paramref name="degrees"/> as if by hand (tests, restoring a saved state).</summary>
        public void SetAngle(float degrees)
        {
            tracker.Set(degrees);
            Apply(feedback: false);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic)
            {
                return;
            }

            var holding = interactorsSelecting.Count >= requiredHands;
            if (holding && !turning)
            {
                turning = true;
                tracker.Begin(GripVector());
            }
            else if (!holding && turning)
            {
                turning = false;
                tracker.End();
                tracker.Set(detents.SnapTarget(tracker.Angle));
                Apply(feedback: true);
                return;
            }

            if (turning)
            {
                tracker.Update(GripVector());
                Apply(feedback: true);
            }
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if (turning)
            {
                tracker.Begin(GripVector()); // a hand joined or swapped: re-anchor so the handle doesn't jump
            }
        }

        // One hand: from the pivot to the hand. Two hands: from the first hand to the second, which turns with the handle.
        private Vector3 GripVector()
        {
            var space = handle.parent;
            var first = LocalAttach(space, 0);
            if (requiredHands > 1 && interactorsSelecting.Count > 1)
            {
                return LocalAttach(space, 1) - first;
            }

            return first - handle.localPosition;
        }

        private Vector3 LocalAttach(Transform space, int index)
        {
            var world = interactorsSelecting[index].GetAttachTransform(this).position;
            return space != null ? space.InverseTransformPoint(world) : world;
        }

        private void Apply(bool feedback)
        {
            handle.localRotation = Quaternion.AngleAxis(tracker.Angle - startAngle, axis) * baseRotation;
            if (!detents.Update(tracker.Angle, out var reached, out var ticked) && !ticked)
            {
                return;
            }

            if (feedback)
            {
                InteractionFeedback.Detent(interactorsSelecting, clickSource, strong: reached != null);
            }

            if (reached != null)
            {
                StateReached?.Invoke(partId, reached);
            }
        }
    }
}
