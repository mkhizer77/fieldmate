using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fieldmate.Procedures
{
    /// <summary>
    /// A procedure as an editable asset (design.md §5.3, #11): ordered steps and safety rules, authored in the Procedure
    /// Editor and turned into the Core <see cref="ProcedureDefinition"/> the runner validates against. The bench scene
    /// uses the code-defined demo unless a <see cref="MachineServices"/> is given one of these.
    /// </summary>
    [CreateAssetMenu(menuName = "Fieldmate/Procedure", fileName = "Procedure")]
    public sealed class ProcedureAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Step
        {
            public string id = "step";
            public string title = "New step";
            public StepKind kind = StepKind.Operate;
            [Tooltip("Inspect/Operate/Measure: the part. Tool: the socket.")] public string partId;
            [Tooltip("Operate: the named state the control must reach, e.g. closed.")] public string targetState;
            [Tooltip("Inspect: seconds of gaze.")] public float dwellSeconds = 1.5f;
            [Tooltip("Tool: the tool to seat in the socket.")] public string toolId;
            [Tooltip("Measure: expected reading.")] public float expectedValue;
            [Tooltip("Measure: accepted ± range.")] public float tolerance = 0.2f;
            [Tooltip("Measure: unit, e.g. bar.")] public string unit = "bar";
        }

        [Serializable]
        public sealed class Rule
        {
            public string id = "rule";
            public string description = "Do this before that.";
            [Tooltip("The part that may not be touched until the requirement holds.")] public string guardedPartId;
            public string requiredPartId;
            public string requiredState;
            [Tooltip("Optional: while the guarded part is in this state the rule lets it go (a closed outlet may always be reopened).")]
            public string exceptWhenGuardedIs;
        }

        public string id = "procedure";
        public string title = "New procedure";
        [Min(0f)] public float timeLimitSeconds = 600f;
        public List<Step> steps = new();
        public List<Rule> rules = new();

        /// <summary>The runnable definition; throws with a readable message when a step can't be built.</summary>
        public ProcedureDefinition ToDefinition()
        {
            var built = new List<StepDefinition>(steps.Count);
            foreach (var s in steps)
            {
                built.Add(s.kind switch
                {
                    StepKind.Inspect => StepDefinition.Inspect(s.id, s.title, s.partId, s.dwellSeconds),
                    StepKind.Operate => StepDefinition.Operate(s.id, s.title, s.partId, s.targetState),
                    StepKind.Tool => StepDefinition.Tool(s.id, s.title, s.partId, s.toolId),
                    StepKind.Measure => StepDefinition.Measure(s.id, s.title, s.partId, s.expectedValue, s.tolerance, s.unit),
                    _ => StepDefinition.Confirm(s.id, s.title),
                });
            }

            var safety = new List<SafetyRule>(rules.Count);
            foreach (var r in rules)
            {
                safety.Add(new SafetyRule(r.id, r.description, r.guardedPartId, r.requiredPartId, r.requiredState, r.exceptWhenGuardedIs));
            }

            return new ProcedureDefinition(id, title, built, safety, timeLimitSeconds);
        }

        /// <summary>Fills this asset from a definition (e.g. the code-defined demo, to start editing from it).</summary>
        public void CopyFrom(ProcedureDefinition definition)
        {
            id = definition.Id;
            title = definition.Title;
            timeLimitSeconds = definition.TimeLimitSeconds;
            steps = new List<Step>();
            foreach (var s in definition.Steps)
            {
                steps.Add(new Step
                {
                    id = s.Id, title = s.Title, kind = s.Kind, partId = s.PartId, targetState = s.TargetState, dwellSeconds = s.DwellSeconds,
                    toolId = s.ToolId, expectedValue = s.ExpectedValue, tolerance = s.Tolerance, unit = s.Unit,
                });
            }

            rules = new List<Rule>();
            foreach (var r in definition.SafetyRules)
            {
                rules.Add(new Rule
                {
                    id = r.Id, description = r.Description, guardedPartId = r.GuardedPartId, requiredPartId = r.RequiredPartId,
                    requiredState = r.RequiredState, exceptWhenGuardedIs = r.ExceptWhenGuardedIs,
                });
            }
        }

        /// <summary>Problems with the procedure itself (empty when it can run): Core validation plus build errors.</summary>
        public IReadOnlyList<string> Problems()
        {
            try
            {
                return ToDefinition().Validate();
            }
            catch (ArgumentException e)
            {
                return new[] { e.Message };
            }
        }
    }
}
