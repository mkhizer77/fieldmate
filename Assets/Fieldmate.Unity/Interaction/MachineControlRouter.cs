using System.Collections.Generic;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.XR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// Connects the machine's controls to the rest of the app: named states and seated tools become
    /// <see cref="InteractionEvent"/>s for the procedure runner (or its initial state before a procedure runs), valve
    /// positions and the breaker drive the telemetry simulation, removing the pump cover opens the relief seat, and the
    /// hand interactors are off while the machine is being placed (the right pinch places it).
    /// </summary>
    public sealed class MachineControlRouter : MonoBehaviour
    {
        [SerializeField] private MachineServices machine;
        [SerializeField] private MachinePlacement placement;
        [SerializeField] private XRBaseInteractor[] interactors;

        private readonly List<IMachineControl> controls = new();
        private readonly List<IMachineControl> continuous = new();
        private readonly List<ToolSocket> sockets = new();

        public IReadOnlyList<IMachineControl> Controls => controls;
        public IReadOnlyList<ToolSocket> Sockets => sockets;

        public void Configure(MachineServices services, MachinePlacement machinePlacement, XRBaseInteractor[] hands)
        {
            machine = services;
            placement = machinePlacement;
            interactors = hands;
        }

        private void Start()
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is IMachineControl control)
                {
                    controls.Add(control);
                    control.StateReached += OnStateReached;
                    if (TwinInputMapper.IsContinuous(control.PartId))
                    {
                        continuous.Add(control);
                    }
                }
            }

            sockets.AddRange(FindObjectsByType<ToolSocket>(FindObjectsSortMode.None));
            foreach (var socket in sockets)
            {
                socket.ToolSocketed += OnToolSocketed;
                socket.socketActive = false; // opened by removing the pump cover
            }

            if (placement != null)
            {
                placement.StateChanged += OnPlacementChanged;
                OnPlacementChanged(placement.State);
            }

            Debug.Log($"[Interaction] {controls.Count} controls, {sockets.Count} sockets");
        }

        private void OnDestroy()
        {
            foreach (var control in controls)
            {
                control.StateReached -= OnStateReached;
            }

            foreach (var socket in sockets)
            {
                socket.ToolSocketed -= OnToolSocketed;
            }

            if (placement != null)
            {
                placement.StateChanged -= OnPlacementChanged;
            }
        }

        private float nextHandLog;

        private void Update()
        {
            LogHands();

            // Valve openings follow the handle continuously; struct updates, no allocations.
            var inputs = machine.Telemetry.Inputs;
            for (var i = 0; i < continuous.Count; i++)
            {
                inputs = TwinInputMapper.Apply(inputs, continuous[i].PartId, continuous[i].State, continuous[i].Normalized);
            }

            machine.Telemetry.Inputs = inputs;
        }

        // Every 5 s while the machine is placed: where the grab points are relative to the head, and whether they move.
        private void LogHands()
        {
            if (interactors == null || interactors.Length == 0 || Time.unscaledTime < nextHandLog || Camera.main == null)
            {
                return;
            }

            nextHandLog = Time.unscaledTime + 5f;
            var head = Camera.main.transform;
            var sb = new System.Text.StringBuilder("[Interaction] hands");
            foreach (var interactor in interactors)
            {
                if (interactor == null) continue;
                var local = head.InverseTransformPoint(interactor.transform.position);
                sb.Append(' ').Append(interactor.name).Append(interactor.enabled ? "" : " (off)")
                    .Append(" at ").Append(local.ToString("0.00")).Append(" from head");
            }

            Debug.Log(sb.ToString());
        }

        private void OnStateReached(string partId, string state)
        {
            Debug.Log($"[Interaction] {partId} → {state}");
            var control = Find(partId);
            machine.Telemetry.Inputs = TwinInputMapper.Apply(machine.Telemetry.Inputs, partId, state, control?.Normalized ?? 0f);

            if (partId == "pump_cover")
            {
                foreach (var socket in sockets)
                {
                    socket.socketActive = state == RemovablePart.Removed;
                }
            }

            if (machine.Runner.State == RunnerState.Running)
            {
                machine.Runner.Handle(InteractionEvent.State(machine.Now, partId, state));
            }
            else
            {
                machine.Runner.SetInitialState(partId, state); // keeps the runner in step before a procedure starts
            }
        }

        private void OnToolSocketed(string socketId, string toolId)
        {
            Debug.Log($"[Interaction] {toolId} seated in {socketId}");
            machine.Runner.Handle(InteractionEvent.Socketed(machine.Now, socketId, toolId));
        }

        private void OnPlacementChanged(PlacementState state)
        {
            var enable = state != PlacementState.Placing;
            if (interactors == null)
            {
                return;
            }

            foreach (var interactor in interactors)
            {
                if (interactor != null)
                {
                    interactor.enabled = enable;
                }
            }
        }

        private IMachineControl Find(string partId)
        {
            foreach (var control in controls)
            {
                if (control.PartId == partId)
                {
                    return control;
                }
            }

            return null;
        }
    }
}
