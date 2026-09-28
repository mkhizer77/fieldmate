using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fieldmate.Interaction
{
    /// <summary>A loose tool or spare part the user carries to a <see cref="ToolSocket"/> (e.g. the relief cartridge).</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ToolItem : XRGrabInteractable
    {
        [SerializeField] private string toolId;

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
        }
    }
}
