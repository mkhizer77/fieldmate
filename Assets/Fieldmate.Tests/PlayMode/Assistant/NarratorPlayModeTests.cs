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
    public IEnumerator Speaks_again_when_the_interlock_holds_a_part_and_when_a_finished_step_is_undone()
    {
        // Device test 2026-10-02 (#90): five held breaker attempts and the breaker going back off, all in silence.
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        var machine = Object.FindAnyObjectByType<MachineServices>();
        var loop = Object.FindAnyObjectByType<VoiceLoop>();
        var panel = Object.FindAnyObjectByType<AssistantPanel>();
        var placement = Object.FindAnyObjectByType<MachinePlacement>();
        loop.Configure(new Silent(), null, null);
        yield return placement.ConfirmAsync(new Pose(new Vector3(0f, 0f, 1.8f), Quaternion.Euler(0f, 180f, 0f)));
        Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Start Button").Press();
        yield return null;

        var runner = machine.Runner;
        runner.Handle(InteractionEvent.Attempt(100, "inlet_valve")); // first time: the violation
        yield return null;
        yield return null;
        Assert.That(panel.TranscriptText, Does.Contain("Hold on. Lock out the breaker before touching the inlet valve."));
        runner.Handle(InteractionEvent.Attempt(101, "inlet_valve"));
        yield return null;
        Assert.That(panel.TranscriptText, Does.Not.Contain("Not yet."), "an immediate retry isn't nagged");
        runner.Handle(InteractionEvent.Attempt(100 + SafetyLineGate.RepeatSeconds + 1, "inlet_valve"));
        yield return null;
        yield return null;
        Assert.That(panel.TranscriptText, Does.Contain("Not yet. Lock out the breaker before touching the inlet valve."), "held again later: said again");

        double clock = 0;
        ProcedureReplay.AdvanceTo(runner, "verify_running", ref clock);
        runner.Handle(InteractionEvent.State(clock + 1, "main_breaker", "off"));
        yield return null;
        yield return null;
        Assert.That(panel.TranscriptText, Does.Contain("Careful, the main breaker isn't on any more."));
    }

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

        // Out of order without a safety rule: the breaker to ON (step 9's target) while step 2 wants LOCKED. The inlet goes
        // back open first, or inlet_before_power (#86) rightly makes it a violation instead.
        machine.Runner.Handle(InteractionEvent.State(machine.Now, "inlet_valve", "open"));
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
