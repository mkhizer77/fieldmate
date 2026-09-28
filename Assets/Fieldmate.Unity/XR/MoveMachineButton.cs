using Fieldmate.Procedures;
using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>
    /// "Move machine" on the skid: starts placement again without controllers (Y on the left controller does the same).
    /// Needed when part of the machine ends up inside a real wall and occlusion hides it (device test 2026-09-28).
    /// </summary>
    public sealed class MoveMachineButton : MonoBehaviour
    {
        [SerializeField] private PressButton button;
        [SerializeField] private MachinePlacement placement;

        public void Configure(PressButton pressButton, MachinePlacement machinePlacement)
        {
            button = pressButton;
            placement = machinePlacement;
        }

        private void Start()
        {
            button.SetLabel("Move machine");
            button.Pressed += OnPressed;
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.Pressed -= OnPressed;
            }
        }

        private void OnPressed()
        {
            Debug.Log("[Placement] move requested from the machine's button");
            placement.BeginPlacing();
        }
    }
}
