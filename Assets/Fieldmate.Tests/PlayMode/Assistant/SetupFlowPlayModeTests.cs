using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>#71: room scan → the mate introduces itself and is placed → the machine is placed → briefing → step 1.</summary>
public class SetupFlowPlayModeTests
{
    [UnitySetUp]
    public IEnumerator LoadWithSetup()
    {
        SetupFlow.AutoRun = true;
        Time.timeScale = 8f; // captions stay up for their reading time; no voice in tests
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
    }

    [TearDown]
    public void Restore()
    {
        SetupFlow.AutoRun = false;
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator Setup_RunsOneStageAtATime_ThenStartsTheProcedure()
    {
        var flow = Object.FindAnyObjectByType<SetupFlow>();
        var mate = Object.FindAnyObjectByType<HologramMate>();
        var panel = Object.FindAnyObjectByType<AssistantPanel>();
        var placement = Object.FindAnyObjectByType<MachinePlacement>();
        var machine = Object.FindAnyObjectByType<MachineServices>();
        var loop = Object.FindAnyObjectByType<VoiceLoop>();
        loop.Configure(new Silent(), null, null); // captions only
        var skid = Object.FindAnyObjectByType<Fieldmate.Interaction.RemovablePart>().transform.root;

        Assert.That(flow.Stage, Is.EqualTo(SetupStage.RoomScan));
        Assert.That(flow.MachineHidden, Is.True, "no machine before its stage");
        Assert.That(skid.GetComponentsInChildren<Renderer>().Any(r => r.enabled), Is.False);
        Assert.That(mate.IsVisible, Is.False, "the mate waits for the room");
        Assert.That(placement.State, Is.EqualTo(PlacementState.Loading), "placement waits for the setup");

        Object.FindAnyObjectByType<SceneScanBootstrap>().Skip();
        yield return new WaitUntil(() => flow.Stage == SetupStage.PlaceMate);
        Assert.That(mate.IsVisible, Is.True);
        Assert.That(panel.TranscriptText, Does.Contain("Hi, I'm Fieldmate"));
        Assert.That(placement.State, Is.EqualTo(PlacementState.Loading), "the machine still waits");

        var spot = mate.transform.position + new Vector3(0.3f, 0f, 0f);
        mate.Place(spot);
        yield return new WaitForSeconds(1f);
        flow.ConfirmMate();
        yield return new WaitUntil(() => flow.Stage == SetupStage.PlaceMachine);
        Assert.That(flow.MachineHidden, Is.False, "the machine appears for placement");
        Assert.That(placement.State, Is.EqualTo(PlacementState.Placing));
        Assert.That(panel.TranscriptText, Does.Contain("let's bring in the machine"));

        yield return placement.ConfirmAsync(new Pose(new Vector3(0f, 0f, 1.8f), Quaternion.Euler(0f, 180f, 0f)));
        yield return new WaitUntil(() => flow.Stage == SetupStage.Briefing);
        Assert.That(panel.TranscriptText, Does.Contain("Today we're going to replace the relief valve cartridge"));
        Assert.That(machine.Runner.State, Is.Not.EqualTo(RunnerState.Running), "not before the briefing ends");

        yield return new WaitUntil(() => flow.Stage == SetupStage.Done);
        Assert.That(machine.Runner.State, Is.EqualTo(RunnerState.Running));
        Assert.That(machine.Runner.CurrentStep.Id, Is.EqualTo("inspect"));
    }

    private sealed class Silent : IChatModel
    {
        public string Name => "silent";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ChatResponse("", null, StopReason.EndTurn));
    }
}
