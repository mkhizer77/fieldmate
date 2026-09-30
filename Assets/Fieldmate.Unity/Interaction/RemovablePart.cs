using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// A part that is taken off by hand, like the pump access cover. It is "fitted" at its home pose and becomes "removed"
    /// once pulled more than <see cref="removeDistance"/> away. Released close to home, it snaps back and is fitted again;
    /// released elsewhere, it stays there (kinematic, no gravity), still parented to the machine. While an
    /// <see cref="Interlock"/> holds it, a grab doesn't move it (#65).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HoverTint))]
    public sealed class RemovablePart : XRGrabInteractable, IMachineControl
    {
        public const string Fitted = "fitted";
        public const string Removed = "removed";

        [SerializeField] private string partId;
        [SerializeField] private float removeDistance = 0.12f;
        [SerializeField] private float snapDistance = 0.06f;

        private Transform home; // the machine; XRI may unparent the part while it is held
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private AudioSource clickSource;
        private string state = Fitted;

        public string PartId => partId;
        public string State => state;
        public float Normalized => state == Removed ? 1f : 0f;

        /// <summary>Distance from the home pose, in the machine's space.</summary>
        public float Offset => Vector3.Distance(home != null ? home.InverseTransformPoint(transform.position) : transform.position, homePosition);

        public event Action<string, string> StateReached;

        public IInterlock Interlock { get; set; }

        public event Action<string> Refused;

        /// <summary>True while a grab is being held still by the interlock.</summary>
        public bool IsHeldByInterlock { get; private set; }

        public void Configure(string part) => partId = part;

        protected override void Awake()
        {
            base.Awake();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            movementType = MovementType.Instantaneous;
            throwOnDetach = false;
            home = transform.parent;
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            clickSource = InteractionFeedback.CreateSource(gameObject);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Late && isSelected && state == Fitted && Offset > removeDistance)
            {
                SetState(Removed);
            }
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            // Decided before XRI starts tracking the hand, so a held part never moves at all.
            var first = !isSelected;
            if (first && Interlock != null && !Interlock.Allows(partId))
            {
                IsHeldByInterlock = true;
                SetTracking(false);
            }

            base.OnSelectEntering(args);
            if (first && IsHeldByInterlock)
            {
                InteractionFeedback.Refused(interactorsSelecting);
                Refused?.Invoke(partId);
            }
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (!isSelected && IsHeldByInterlock)
            {
                IsHeldByInterlock = false;
                SetTracking(true);
            }

            if (!isSelected && Offset <= snapDistance)
            {
                ReturnHome();
            }
        }

        private void SetTracking(bool follow)
        {
            trackPosition = follow;
            trackRotation = follow;
            trackScale = follow;
            unparentTransformOnGrab = follow;
        }

        /// <summary>Puts the part back on its seat (release near home, or a reset).</summary>
        public void ReturnHome()
        {
            if (transform.parent != home)
            {
                transform.SetParent(home, false);
            }

            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            SetState(Fitted);
        }

        private void SetState(string next)
        {
            if (state == next)
            {
                return;
            }

            state = next;
            InteractionFeedback.Detent(interactorsSelecting, clickSource, strong: true);
            StateReached?.Invoke(partId, next);
        }
    }
}
