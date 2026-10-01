using System.Linq;
using Fieldmate.Editor;
using Fieldmate.Procedures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Procedures;

/// <summary>#11: procedures as assets, validated against the machine, edited in the Procedure Editor.</summary>
public class ProcedureAssetTests
{
    private static ProcedureAsset FromDemo()
    {
        var asset = ScriptableObject.CreateInstance<ProcedureAsset>();
        asset.CopyFrom(DemoProcedures.ReliefValveReplacement());
        return asset;
    }

    [Test]
    public void Demo_round_trips_through_an_asset()
    {
        var demo = DemoProcedures.ReliefValveReplacement();
        var back = FromDemo().ToDefinition();
        Assert.That(back.Id, Is.EqualTo(demo.Id));
        Assert.That(back.Steps.Select(s => (s.Id, s.Kind, s.PartId, s.TargetState, s.ToolId)),
            Is.EqualTo(demo.Steps.Select(s => (s.Id, s.Kind, s.PartId, s.TargetState, s.ToolId))));
        Assert.That(back.SafetyRules.Select(r => r.ToString()), Is.EqualTo(demo.SafetyRules.Select(r => r.ToString())));
        Assert.That(back.TimeLimitSeconds, Is.EqualTo(demo.TimeLimitSeconds));
    }

    [Test]
    public void The_demo_validates_against_the_machine_and_the_manual()
    {
        Assert.That(ProcedureEditorWindow.Validate(FromDemo()), Is.Empty);
    }

    [Test]
    public void Validation_names_missing_parts_sockets_tools_and_unreachable_states()
    {
        var asset = FromDemo();
        asset.steps[1].partId = "flux_capacitor";                       // lockout: no such part
        asset.steps[2].targetState = "sideways";                         // inlet valve: open / closed only
        asset.steps[5].partId = "nowhere_seat";                          // replace: no such socket
        asset.steps[5].toolId = "spanner";                               // ...and no such tool
        asset.rules[0].requiredState = "unplugged";                      // breaker: on / off / locked
        var problems = ProcedureEditorWindow.Validate(asset);

        Assert.That(problems, Has.Some.Contains("no part 'flux_capacitor'"));
        Assert.That(problems, Has.Some.Contains("'inlet_valve' has no state 'sideways' (it has: open, closed)"));
        Assert.That(problems, Has.Some.Contains("no socket 'nowhere_seat'"));
        Assert.That(problems, Has.Some.Contains("no tool 'spanner'"));
        Assert.That(problems, Has.Some.Contains("'main_breaker' has no state 'unplugged' (it has: on, off, locked)"));
    }

    [Test]
    public void A_step_that_cannot_be_built_is_a_problem_not_an_exception()
    {
        var asset = FromDemo();
        asset.steps[0].partId = null; // inspect needs a part
        Assert.That(asset.Problems(), Is.Not.Empty);
    }

    [Test]
    public void The_editor_creates_duplicates_and_opens_procedures()
    {
        var created = ProcedureEditorWindow.CreateAsset("TestProcedure", fromDemo: true);
        var duplicate = ProcedureEditorWindow.Duplicate(created);
        try
        {
            Assert.That(AssetDatabase.GetAssetPath(created), Does.StartWith(ProcedureEditorWindow.Folder));
            Assert.That(duplicate.steps, Has.Count.EqualTo(8));
            Assert.That(duplicate.id, Is.EqualTo("relief_valve_replacement_copy"));

            // Batch-mode tests have no graphics device to show a window; the window object still loads and selects.
            var window = ScriptableObject.CreateInstance<ProcedureEditorWindow>();
            window.Select(duplicate);
            Object.DestroyImmediate(window);
        }
        finally
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(duplicate));
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(created));
            if (AssetDatabase.FindAssets(string.Empty, new[] { ProcedureEditorWindow.Folder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(ProcedureEditorWindow.Folder);
            }
        }
    }

    [Test]
    public void Replaying_to_the_last_step_breaks_no_safety_rule()
    {
        var runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        runner.SetInitialState("main_breaker", "on");
        runner.SetInitialState("inlet_valve", "open");
        runner.SetInitialState("pump_cover", "fitted");
        var clock = 0d;
        ProcedureReplay.AdvanceTo(runner, "verify_running", ref clock);
        Assert.That(runner.CurrentStep.Id, Is.EqualTo("verify_running"));
        Assert.That(runner.Violations, Is.Empty, "the cover is refitted before the breaker goes back on");
        Assert.That(runner.Errors, Is.Empty);
    }
}
