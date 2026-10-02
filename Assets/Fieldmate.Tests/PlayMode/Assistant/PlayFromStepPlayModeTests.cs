using System.Collections;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#11: Play from step confirms the machine and advances the procedure to the chosen step.</summary>
public class PlayFromStepPlayModeTests
{
    [UnityTest]
    public IEnumerator Starts_the_procedure_at_the_chosen_step()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        var machine = Object.FindAnyObjectByType<MachineServices>();
        var placement = Object.FindAnyObjectByType<MachinePlacement>();

        PlayFromStep.Run("verify_zero");
        yield return new WaitUntil(() => machine.Runner.State == RunnerState.Running);
        yield return null;

        Assert.That(placement.State, Is.EqualTo(PlacementState.Placed));
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("verify_zero"));
        Assert.That(machine.Runner.Violations, Is.Empty, "the steps before it were done in order");
        Assert.That(Object.FindAnyObjectByType<ProcedurePanel>().TitleText, Does.StartWith("Step 4 of 8"));
    }
}
