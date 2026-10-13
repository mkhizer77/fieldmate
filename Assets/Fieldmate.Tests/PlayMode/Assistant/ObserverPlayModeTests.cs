using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Fieldmate.Assistant;
using Fieldmate.Net;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>
/// #26 end to end over real Netcode for GameObjects on this machine: the bench shares (local network, no Relay), the
/// observer dashboard joins by "127.0.0.1:port", sees the twin, procedure and transcript, and its commands reach the twin.
/// </summary>
public class ObserverPlayModeTests
{
    private const ushort TestPort = 7791;

    private TraineeObserverHost host;
    private MachineServices machine;
    private GameObject observerGo;
    private ObserverDashboard dashboard;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        host = UnityEngine.Object.FindAnyObjectByType<TraineeObserverHost>();
        machine = UnityEngine.Object.FindAnyObjectByType<MachineServices>();
        host.PreferRelay = false; // tests never reach Unity Gaming Services
        host.GetComponent<ObserverNetwork>().Port = TestPort;
    }

    [UnityTearDown]
    public IEnumerator Clean()
    {
        if (observerGo != null)
        {
            UnityEngine.Object.Destroy(observerGo);
        }

        host?.Stop();
        yield return null;
    }

    private static IEnumerator Await(Task task)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
    }

    private static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > until)
            {
                Assert.Fail($"timed out waiting for {what}");
            }

            yield return null;
        }
    }

    private IEnumerator ShareAndJoin()
    {
        var sharing = host.StartSharingAsync();
        yield return Await(sharing);
        Assert.That(host.JoinCode, Does.EndWith($":{TestPort}"), "local network code is ip:port");

        observerGo = new GameObject("Test Observer", typeof(ObserverNetwork));
        dashboard = observerGo.AddComponent<ObserverDashboard>();
        dashboard.Configure(observerGo.GetComponent<ObserverNetwork>());
        dashboard.Join($"127.0.0.1:{TestPort}");
        yield return WaitFor(() => dashboard.Client is { HasSnapshot: true }, 10f, "the first snapshot");
    }

    [UnityTest]
    public IEnumerator The_hand_menu_has_an_observer_button()
    {
        var button = UnityEngine.Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Observer Button");
        Assert.That(button.transform.IsChildOf(UnityEngine.Object.FindAnyObjectByType<Fieldmate.XR.HandMenu>().transform), Is.True);
        Assert.That(button.Label, Is.EqualTo("Observer: Off"));
        yield break;
    }

    [UnityTest]
    public IEnumerator An_observer_joins_and_sees_the_twin_the_procedure_and_the_conversation()
    {
        yield return ShareAndJoin();

        var client = dashboard.Client;
        Assert.That(client.Hello.MachineName, Is.EqualTo(machine.Manual.MachineName));
        Assert.That(client.Snapshot.ProcedureTitle, Is.EqualTo(machine.Runner.Definition.Title));
        Assert.That(dashboard.ProcedureText, Does.Contain(machine.Runner.Definition.Title));
        Assert.That(host.Server.ObserverCount, Is.EqualTo(1));

        var pressure = machine.Telemetry[TelemetryChannel.Pressure];
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(client.Snapshot.Telemetry[(int)TelemetryChannel.Pressure], Is.EqualTo(machine.Telemetry[TelemetryChannel.Pressure]).Within(0.5f),
            $"streamed pressure follows the twin (was {pressure:0.0})");

        UnityEngine.Object.FindAnyObjectByType<VoiceLoop>().Narrate("Let's lock out the breaker.", interrupt: false);
        yield return WaitFor(() => client.Transcript.Any(l => l.Text == "Let's lock out the breaker."), 5f, "the transcript line");
        yield return null;
        Assert.That(dashboard.TranscriptText, Does.Contain("Let's lock out the breaker."));
    }

    [UnityTest]
    public IEnumerator Observer_commands_reach_the_twin_and_the_trainee()
    {
        yield return ShareAndJoin();
        var faults = machine.Telemetry.Faults;
        Assert.That(faults.IsActive(FaultModel.Overheating), Is.False);

        dashboard.Send(ObserverCommand.InjectFault(FaultModel.Overheating));
        yield return WaitFor(() => faults.IsActive(FaultModel.Overheating), 5f, "the injected fault");
        yield return WaitFor(() => dashboard.Client.IsFaultActive(FaultModel.Overheating), 5f, "the fault in the next snapshot");
        Assert.That(dashboard.Client.LastResult?.Accepted, Is.True);

        dashboard.Send(ObserverCommand.SendMessage("Take your time with the lockout."));
        yield return WaitFor(() => dashboard.Client.Transcript.Any(l => l.Text.Contains("Take your time with the lockout.")), 5f,
            "the message spoken in the headset, echoed in the transcript");
    }

    [UnityTest]
    public IEnumerator Stopping_disconnects_the_observer_and_the_headset_keeps_running()
    {
        yield return ShareAndJoin();

        host.Stop();
        yield return WaitFor(() => !dashboard.Client.Connected, 5f, "the observer to notice");
        Assert.That(host.IsSharing, Is.False);
        Assert.That(machine.Runner, Is.Not.Null);
        yield return new WaitForSecondsRealtime(0.5f);
        StringAssert.Contains("Disconnected", dashboard.StatusText);
    }

    [UnityTest]
    public IEnumerator The_preview_runs_the_dashboard_against_a_simulated_trainee()
    {
        observerGo = new GameObject("Preview Observer", typeof(ObserverNetwork));
        var view = observerGo.AddComponent<ObserverDashboard>();
        view.Configure(observerGo.GetComponent<ObserverNetwork>());
        var preview = observerGo.AddComponent<ObserverPreview>();
        preview.Configure(view);

        preview.Run();
        yield return new WaitForSecondsRealtime(0.3f);

        Assert.That(view.Client.HasSnapshot, Is.True);
        Assert.That(view.ProcedureText, Does.Contain("Step 3 of"));
        Assert.That(view.TranscriptText, Does.Contain("Why is the pressure so high?"));
        Assert.That(view.Client.IsFaultActive(FaultModel.Overpressure), Is.True);
        view.Send(ObserverCommand.InjectFault(FaultModel.LooseMount));
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(view.Client.IsFaultActive(FaultModel.LooseMount), Is.True, "commands work against the simulated twin");
    }

    [Test]
    public void Join_codes_are_told_apart_from_addresses()
    {
        Assert.That(ObserverNetwork.TryParseAddress("192.168.1.20:7788", out var a, out var p), Is.True);
        Assert.That((a, p), Is.EqualTo(("192.168.1.20", (ushort)7788)));
        Assert.That(ObserverNetwork.TryParseAddress("10.0.0.5", out _, out var defaultPort), Is.True);
        Assert.That(defaultPort, Is.EqualTo(ObserverNetwork.DefaultPort));
        Assert.That(ObserverNetwork.TryParseAddress("K7Q2XB", out _, out _), Is.False, "a Relay code");
        Assert.That(ObserverNetwork.TryParseAddress("10.0.0.5:0", out _, out _), Is.False);
        Assert.That(ObserverNetwork.TryParseAddress("", out _, out _), Is.False);
    }
}
