using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.AI;

/// <summary>#16: two failures in a row switch the session to scripted answers; a working model switches it back.</summary>
public class SessionOfflineTests
{
    private ScriptedChat chat;
    private AssistantSession session;
    private FakeScene scene;
    private System.Collections.Generic.List<TranscriptEntry> transcript;
    private System.Collections.Generic.List<bool> offlineEvents;

    [SetUp]
    public void SetUp()
    {
        var manual = MachineManual.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Manual", "manual.json")));
        var runner = new ProcedureRunner(DemoProcedures.ReliefValveReplacement());
        var telemetry = new TelemetryModel(FaultModel.CreateDefault());
        scene = new FakeScene();
        var tools = new AssistantTools(manual, runner, telemetry, scene);
        var registry = FieldmateTools.CreateRegistry(includeVision: false);
        tools.AttachTo(registry);
        chat = new ScriptedChat();
        var clock = new SteppingClock();
        session = new AssistantSession(chat, new FakeStt(), new FakeTts(), registry, new ConversationState(), _ => "sys", _ => "ctx", clock.Read)
        {
            Fallback = new FallbackResponses(manual, runner, telemetry),
        };
        transcript = new();
        offlineEvents = new();
        session.TranscriptAdded += transcript.Add;
        session.OfflineChanged += offlineEvents.Add;
    }

    private Task<TurnResult> Ask(string text) => session.RunTextTurnAsync(text, new RecordingSink(), CancellationToken.None);

    [Test]
    public async Task Two_failures_switch_to_scripted_answers_without_calling_the_model()
    {
        chat.Throw(ProviderException.Network);
        var first = await Ask("hello");
        Assert.That(first.Success, Is.False);
        Assert.That(session.Offline, Is.False, "one failure is just an error");

        chat.Throw(ProviderException.Network);
        var second = await Ask("where is the relief valve?");
        Assert.That(session.Offline, Is.True);
        Assert.That(offlineEvents, Is.EqualTo(new[] { true }));
        Assert.That(second.UsedFallback, Is.True, "the failing turn itself is answered");
        Assert.That(second.AssistantText, Does.Contain("highlighted"));
        Assert.That(scene.Highlighted, Does.Contain("relief_valve"), "the scripted answer still acts in the scene");

        var third = await Ask("what now?"); // no reply queued: the model must not be called
        Assert.That(third.UsedFallback, Is.True);
        Assert.That(third.AssistantText, Does.Contain("Press Start"));
        Assert.That(chat.Requests, Has.Count.EqualTo(2), "no model call while offline");
    }

    [Test]
    public async Task A_working_model_on_the_retry_turn_brings_it_back_online()
    {
        chat.Throw(ProviderException.Timeout).Throw(ProviderException.Timeout);
        await Ask("a");
        await Ask("b");
        Assert.That(session.Offline, Is.True);
        await Ask("c"); // offline turn 1, scripted
        await Ask("d"); // offline turn 2, scripted
        chat.Say("Back with you.");
        var retry = await Ask("e"); // offline turn 3: real attempt
        Assert.That(retry.UsedFallback, Is.False);
        Assert.That(retry.AssistantText, Is.EqualTo("Back with you."));
        Assert.That(session.Offline, Is.False);
        Assert.That(offlineEvents, Is.EqualTo(new[] { true, false }));
    }
}
