using System.Collections;
using System.Linq;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#61: the assistant announces a completed step and interrupts on a violation, without a voice prompt.</summary>
public class NarratorPlayModeTests
{
    [UnityTest]
    public IEnumerator Narrates_step_completion_and_violations()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        var machine = Object.FindAnyObjectByType<MachineServices>();
        var loop = Object.FindAnyObjectByType<VoiceLoop>();
        var panel = Object.FindAnyObjectByType<AssistantPanel>();
        var placement = Object.FindAnyObjectByType<MachinePlacement>();
        loop.Configure(new Silent(), null, null); // no model, no voice: narration still lands on the panel
        yield return placement.ConfirmAsync(new Pose(new Vector3(0f, 0f, 1.8f), Quaternion.Euler(0f, 180f, 0f)));
        Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Start Button").Press();
        yield return null;
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("inspect"));

        machine.Runner.Handle(InteractionEvent.Gaze(machine.Now, "relief_valve", 3f));
        yield return null;
        yield return null;
        Assert.That(panel.TranscriptText, Does.Contain("Nice work. Now let's lock out the main breaker."));

        Assert.That(machine.Runner.HelpRequests, Is.EqualTo(0), "device test 2026-10-01: the mate's own lines are not help the user asked for");

        machine.Runner.Handle(InteractionEvent.State(machine.Now, "inlet_valve", "closed")); // before lockout
        yield return null;
        yield return null;
        Assert.That(panel.TranscriptText, Does.Contain("Hold on. Lock out the breaker before touching the inlet valve."));

        // Out of order without a safety rule: the breaker to ON (step 7's target) while step 2 wants LOCKED.
        machine.Runner.Handle(InteractionEvent.State(machine.Now, "main_breaker", "on"));
        yield return null;
        yield return null;
        Assert.That(panel.TranscriptText, Does.Contain("comes a bit later").And.Contain("First, let's lock out the main breaker."), "the out-of-order action is explained and the current step repeated");
    }

    private sealed class Silent : IChatModel
    {
        public string Name => "silent";
        public System.Threading.Tasks.Task<ChatResponse> CompleteAsync(ChatRequest request, System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(new ChatResponse("", null, StopReason.EndTurn));
    }
}
