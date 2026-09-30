using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// A part that is taken off by hand, like the pump access cover. It is "fitted" at its home pose and becomes "removed"
    /// once pulled more than <see cref="removeDistance"/> away. Released close to home, it snaps back and is fitted again;
    /// released elsewhere, it stays there (kinematic, no gravity), still parented to the machine. Brought back within
    /// <see cref="clickDistance"/> while still held, it clicks on by itself (#67). It is grabbed where the hand touches it
    /// and keeps its rotation relative to the hand (dynamic attach), and distances are measured at its centre, not its
    /// pivot (the builder's pivot is the machine origin).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HoverTint))]
    public sealed class RemovablePart : XRGrabInteractable, IMachineControl
    {
        public const string Fitted = "fitted";
        public const string Removed = "removed";

        [SerializeField] private string partId;
        [SerializeField] private float removeDistance = 0.12f;
        [SerializeField] private float snapDistance = 0.15f;
        [SerializeField] private float clickDistance = 0.05f;

        private Transform home; // the machine; XRI may unparent the part while it is held
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 localCentre; // the part's visual centre in its own space
        private Vector3 seatedCentre; // ...and in the machine's space when fitted
        private bool clickPending;
        private AudioSource clickSource;
        private string state = Fitted;

        public string PartId => partId;
        public string State => state;
        public float Normalized => state == Removed ? 1f : 0f;

        /// <summary>How far the part's centre is from its seated position, in the machine's space.</summary>
        public float Offset
        {
            get
            {
                var centre = transform.TransformPoint(localCentre);
                return Vector3.Distance(home != null ? home.InverseTransformPoint(centre) : centre, seatedCentre);
            }
        }

        /// <summary>The part's visual centre in world space (where a hand takes it).</summary>
        public Vector3 Centre => transform.TransformPoint(localCentre);

        public event Action<string, string> StateReached;

        public void Configure(string part) => partId = part;

        protected override void Awake()
        {
            base.Awake();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            movementType = MovementType.Instantaneous;
            throwOnDetach = false;
            useDynamicAttach = true; // held where the hand is, turning with it; no jump to the pivot
            matchAttachPosition = true;
            matchAttachRotation = true;
            snapToColliderVolume = false;
            localCentre = LocalCentre();
            home = transform.parent;
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            seatedCentre = home != null ? home.InverseTransformPoint(Centre) : Centre;
            clickSource = InteractionFeedback.CreateSource(gameObject);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Late || !isSelected)
            {
                return;
            }

            if (state == Fitted && Offset > removeDistance)
            {
                SetState(Removed);
            }
            else if (state == Removed && Offset < clickDistance)
            {
                clickPending = true; // released after the manager's update, not inside it
            }
        }

        private void LateUpdate()
        {
            if (!clickPending)
            {
                return;
            }

            clickPending = false;
            if (isSelected)
            {
                interactionManager.CancelInteractableSelection((IXRSelectInteractable)this); // the exit puts it home
            }
        }

        private Vector3 LocalCentre()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return Vector3.zero;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return transform.InverseTransformPoint(bounds.center);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (!isSelected && Offset <= snapDistance)
            {
                ReturnHome();
            }
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
