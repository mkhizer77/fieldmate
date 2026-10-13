using System.Collections.Generic;
using System.Linq;
using Fieldmate.Interaction;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>
/// Checks an authored procedure against the machine it runs on (#11): the procedure itself (Core validation), then
/// that every part, socket and tool it names exists in the machine prefab and the manual, and that every target state
/// is one the control can actually reach (a rotary's named detents, a cover's fitted/removed).
/// </summary>
public static class ProcedureValidator
{
    public static IReadOnlyList<string> Check(ProcedureAsset asset, GameObject machine, MachineManual manual)
    {
        var problems = new List<string>(asset.Problems());
        var parts = machine.GetComponentsInChildren<PartTag>(true).Select(p => p.PartId).ToHashSet();
        var sockets = machine.GetComponentsInChildren<ToolSocket>(true).Select(s => s.SocketId).ToHashSet();
        var tools = machine.GetComponentsInChildren<ToolItem>(true).Select(t => t.ToolId).ToHashSet();
        var catalog = PartCatalog.FromManual(manual);

        foreach (var step in asset.steps)
        {
            var at = $"Step '{step.id}'";
            switch (step.kind)
            {
                case StepKind.Tool:
                    if (!sockets.Contains(step.partId)) problems.Add($"{at}: no socket '{step.partId}' on the machine (sockets: {Join(sockets)}).");
                    if (!tools.Contains(step.toolId)) problems.Add($"{at}: no tool '{step.toolId}' on the machine (tools: {Join(tools)}).");
                    break;
                case StepKind.Confirm:
                    break;
                default:
                    if (!parts.Contains(step.partId)) problems.Add($"{at}: no part '{step.partId}' on the machine.");
                    else if (!catalog.TryGet(step.partId, out _)) problems.Add($"{at}: part '{step.partId}' is not in the manual.");
                    break;
            }

            if (step.kind == StepKind.Operate && parts.Contains(step.partId) && !CanReach(machine, step.partId, step.targetState, out var states))
            {
                problems.Add($"{at}: '{step.partId}' has no state '{step.targetState}' (it has: {states}).");
            }
        }

        foreach (var rule in asset.rules)
        {
            var at = $"Rule '{rule.id}'";
            if (!parts.Contains(rule.guardedPartId) && !sockets.Contains(rule.guardedPartId)) problems.Add($"{at}: no part '{rule.guardedPartId}' to guard.");
            if (!parts.Contains(rule.requiredPartId)) problems.Add($"{at}: no part '{rule.requiredPartId}'.");
            else if (!CanReach(machine, rule.requiredPartId, rule.requiredState, out var states))
            {
                problems.Add($"{at}: '{rule.requiredPartId}' has no state '{rule.requiredState}' (it has: {states}).");
            }

            if (!string.IsNullOrWhiteSpace(rule.exceptWhenGuardedIs) && parts.Contains(rule.guardedPartId)
                && !CanReach(machine, rule.guardedPartId, rule.exceptWhenGuardedIs, out var guardedStates))
            {
                problems.Add($"{at}: '{rule.guardedPartId}' has no state '{rule.exceptWhenGuardedIs}' (it has: {guardedStates}).");
            }
        }

        return problems;
    }

    /// <summary>Every part id on the machine, for the editor's part picker.</summary>
    public static string[] PartIds(GameObject machine) =>
        machine.GetComponentsInChildren<PartTag>(true).Select(p => p.PartId)
            .Concat(machine.GetComponentsInChildren<ToolSocket>(true).Select(s => s.SocketId))
            .Distinct().OrderBy(id => id).ToArray();

    // Whether the part's control can reach the state; controls without named states accept anything.
    private static bool CanReach(GameObject machine, string partId, string state, out string states)
    {
        var rotary = machine.GetComponentsInChildren<RotaryInteractable>(true).FirstOrDefault(r => r.PartId == partId);
        if (rotary != null)
        {
            states = Join(rotary.DetentStates);
            return rotary.TryGetDetentAngle(state, out _);
        }

        if (machine.GetComponentsInChildren<RemovablePart>(true).Any(r => r.PartId == partId))
        {
            states = $"{RemovablePart.Fitted}, {RemovablePart.Removed}";
            return state == RemovablePart.Fitted || state == RemovablePart.Removed;
        }

        states = "any";
        return true;
    }

    private static string Join(IEnumerable<string> items) => string.Join(", ", items);
}
