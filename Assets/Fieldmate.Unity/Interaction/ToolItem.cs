using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Interaction
{
    /// <summary>A loose tool or spare part the user carries to a <see cref="ToolSocket"/> (e.g. the relief cartridge).</summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HoverTint))]
    public sealed class ToolItem : XRGrabInteractable
    {
        [SerializeField] private string toolId;

        private Transform home;
        private Vector3 homePosition;
        private Quaternion homeRotation;

        public string ToolId => toolId;

        public void Configure(string tool) => toolId = tool;

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
        }

        /// <summary>Puts the tool back where it started (a new run of the procedure).</summary>
        public void ReturnHome()
        {
            if (transform.parent != home)
            {
                transform.SetParent(home, false);
            }

            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
        }
    }
}
