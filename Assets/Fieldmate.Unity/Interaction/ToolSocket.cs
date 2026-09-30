using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// A seat that takes a <see cref="ToolItem"/> (design.md §5.3 Tool steps). Only tools are accepted, so the user can't
    /// socket a valve handle; which tool is right is the procedure's call (a wrong one is an error there, not a refusal
    /// here). Inactive until opened (e.g. the relief seat behind the pump cover).
    /// </summary>
    public sealed class ToolSocket : XRSocketInteractor
    {
        [SerializeField] private string socketId;

        public string SocketId => socketId;

        /// <summary>The tool currently seated, if any.</summary>
        public ToolItem Seated { get; private set; }

        /// <summary>Raised with (socket id, tool id) when a tool is seated.</summary>
        public event Action<string, string> ToolSocketed;

        public void Configure(string socket) => socketId = socket;

        public override bool CanHover(IXRHoverInteractable interactable) => interactable is ToolItem && base.CanHover(interactable);

        public override bool CanSelect(IXRSelectInteractable interactable) => interactable is ToolItem && base.CanSelect(interactable);

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if (args.interactableObject is ToolItem tool)
            {
                Seated = tool;
                ToolSocketed?.Invoke(socketId, tool.ToolId);
            }
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (ReferenceEquals(args.interactableObject, Seated))
            {
                Seated = null;
            }
        }
    }
}
