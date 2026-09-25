using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

public class AssistantSessionTests
{
    private ScriptedChat chat;
    private FakeStt stt;
    private FakeTts tts;
    private ToolRegistry registry;
    private AssistantSession session;
    private List<AssistantState> states;
    private List<TranscriptEntry> transcript;
    private List<string> executed;

    [SetUp]
    public void SetUp()
    {
        chat = new ScriptedChat();
        stt = new FakeStt();
        tts = new FakeTts();
        registry = FieldmateTools.CreateRegistry();
        executed = new List<string>();
        foreach (var definition in registry.Definitions)
        {
            var name = definition.Name;
            registry.SetExecutor(name, new DelegateToolExecutor((call, _) => { executed.Add(name); return ToolResult.Success(call, $"{name} ok"); }));
        }

        var clock = new SteppingClock();
        session = new AssistantSession(chat, stt, tts, registry, new ConversationState(), lang => $"system-{lang}",
            text => $"context for: {text}", clock.Read);
        states = new List<AssistantState>();
        transcript = new List<TranscriptEntry>();
        session.StateChanged += states.Add;
        session.TranscriptAdded += transcript.Add;
    }

    private static AudioData Audio() => new(new float[16000], 16000);

    [Test]
    public async Task AudioTurn_TranscribesThinksAndSpeaks()
    {
        chat.Say("The relief valve is stuck [fault.overpressure].");
        var sink = new RecordingSink();

        Assert.That(session.BeginListening(), Is.True);
        var result = await session.RunAudioTurnAsync(Audio(), sink, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.UserText, Is.EqualTo("Why is the pressure high?"));
        Assert.That(result.AssistantText, Does.Contain("[fault.overpressure]"));
        Assert.That(tts.Spoken.Single(), Is.EqualTo("The relief valve is stuck."), "citations are not read aloud");
        Assert.That((sink.SampleRate, sink.Samples, sink.Ended), Is.EqualTo((22050, 3, true)));
        Assert.That(states, Is.EqualTo(new[]
        {
            AssistantState.Listening, AssistantState.Transcribing, AssistantState.Thinking, AssistantState.Speaking, AssistantState.Idle,
        }));
        Assert.That(transcript.Select(t => t.Kind), Is.EqualTo(new[] { TranscriptKind.User, TranscriptKind.Assistant }));
    }

    [Test]
    public async Task Request_CarriesSystemPromptToolsAndTurnContext()
    {
        chat.Say("ok");
        session.Language = "de";
        await session.RunTextTurnAsync("Hallo", null, CancellationToken.None);

        var request = chat.Requests.Single();
        Assert.That(request.SystemPrompt, Is.EqualTo("system-de"));
        Assert.That(request.Context, Is.EqualTo("context for: Hallo"));
        Assert.That(request.Tools, Has.Count.EqualTo(8));
        Assert.That(request.MaxOutputTokens, Is.EqualTo(300));
    }

    [Test]
    public async Task ToolCalls_AreExecutedEchoedAndFedBack()
    {
        chat.Call("t1", "highlight_part", "{\"part_id\":\"relief_valve\"}", "Let me show you.")
            .Then(request =>
            {
                Assert.That(request.Messages.Last().Role, Is.EqualTo(ChatRole.Tool));
                Assert.That(request.Context, Is.EqualTo("context for: Where is the relief valve?"), "same context all turn");
                return new ChatResponse("It's highlighted now [part.relief_valve].", null, StopReason.EndTurn);
            });

        var result = await session.RunTextTurnAsync("Where is the relief valve?", new RecordingSink(), CancellationToken.None);

        Assert.That(executed, Is.EqualTo(new[] { "highlight_part" }));
        Assert.That(result.ToolsUsed, Is.EqualTo(new[] { "highlight_part" }));
        Assert.That(transcript.Single(t => t.Kind == TranscriptKind.Tool).Text, Is.EqualTo("highlight_part(part_id=relief_valve) → highlight_part ok"));
        Assert.That(result.Timings.ChatRequests, Is.EqualTo(2));
        Assert.That(session.Conversation.Messages.Select(m => m.Role), Is.EqualTo(new[]
        {
            ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant,
        }));
    }

    [Test]
    public async Task ToolLoop_IsBounded()
    {
        for (var i = 0; i < 10; i++)
        {
            chat.Call($"t{i}", "read_telemetry", "{}");
        }

        var result = await session.RunTextTurnAsync("Loop forever", null, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(chat.Requests, Has.Count.EqualTo(AssistantSession.MaxToolRounds + 2));
        Assert.That(executed, Has.Count.EqualTo(AssistantSession.MaxToolRounds), "later calls answered with 'budget used up'");
        Assert.That(session.Conversation.PendingToolCalls, Is.Empty);
    }

    [Test]
    public async Task EmptyTranscript_AsksToRepeat_WithoutCallingTheModel()
    {
        stt.Text = "  ";
        var result = await session.RunAudioTurnAsync(Audio(), null, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorType, Is.Null);
        Assert.That(chat.Requests, Is.Empty);
        Assert.That(transcript.Single().Kind, Is.EqualTo(TranscriptKind.Info));
        Assert.That(session.State, Is.EqualTo(AssistantState.Idle));
    }

    [Test]
    public async Task SttFailure_ShowsFriendlyErrorAndCountsFailures()
    {
        stt.Error = new ProviderException(ProviderException.Network, "offline");

        var first = await session.RunAudioTurnAsync(Audio(), null, CancellationToken.None);
        await session.RunAudioTurnAsync(Audio(), null, CancellationToken.None);

        Assert.That(first.ErrorType, Is.EqualTo(ProviderException.Network));
        Assert.That(session.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(transcript.Last().Text, Does.Contain("Wi-Fi"));
        Assert.That(session.State, Is.EqualTo(AssistantState.Idle));
    }

    [Test]
    public async Task ChatFailure_RollsBackTheTurn_AndSuccessResetsTheCounter()
    {
        chat.Say("first answer").Throw(ProviderException.Timeout).Say("third answer");
        await session.RunTextTurnAsync("one", null, CancellationToken.None);

        var failed = await session.RunTextTurnAsync("two", null, CancellationToken.None);
        Assert.That(failed.Success, Is.False);
        Assert.That(session.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(session.Conversation.Messages, Has.Count.EqualTo(2), "failed turn removed");

        await session.RunTextTurnAsync("three", null, CancellationToken.None);
        Assert.That(session.ConsecutiveFailures, Is.Zero);
        Assert.That(session.Conversation.Messages.Select(m => m.Text), Is.EqualTo(new[] { "one", "first answer", "three", "third answer" }));
    }

    [Test]
    public async Task TtsQuota_KeepsTheTextAnswer()
    {
        chat.Say("Close the inlet valve.");
        tts.Error = new ProviderException(ProviderException.TtsQuota, "quota");
        var sink = new RecordingSink();

        var result = await session.RunTextTurnAsync("What next?", sink, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(sink.Ended, Is.True);
        Assert.That(transcript.Last().Text, Does.Contain("answering in text"));
        Assert.That(session.ConsecutiveFailures, Is.Zero);
    }

    [Test]
    public async Task NoSink_SkipsSpeech()
    {
        chat.Say("Text only.");
        await session.RunTextTurnAsync("hi", null, CancellationToken.None);

        Assert.That(tts.Spoken, Is.Empty);
        Assert.That(states, Has.No.Member(AssistantState.Speaking));
    }

    [Test]
    public async Task Timings_MeasureFirstAudioFromEndOfSpeech()
    {
        chat.Say("Answer.");
        var result = await session.RunAudioTurnAsync(Audio(), new RecordingSink(), CancellationToken.None);

        var t = result.Timings;
        Assert.That(t.SpeechToText, Is.GreaterThan(0));
        Assert.That(t.Chat, Is.GreaterThan(0));
        Assert.That(t.FirstResponse, Is.GreaterThan(t.SpeechToText));
        Assert.That(t.Total, Is.GreaterThanOrEqualTo(t.FirstResponse));
        Assert.That(t.ToString(), Does.Contain("first response"));
        Assert.That(session.LastTurn, Is.SameAs(result));
    }

    [Test]
    public void Cancellation_RollsBackAndReturnsToIdle()
    {
        chat.Say("never");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => session.RunTextTurnAsync("hi", null, cts.Token));
        Assert.That(session.Conversation.Messages, Is.Empty);
        Assert.That(session.State, Is.EqualTo(AssistantState.Idle));
    }

    [Test]
    public void Listening_OnlyStartsFromIdle_AndCanBeCancelled()
    {
        Assert.That(session.BeginListening(), Is.True);
        Assert.That(session.BeginListening(), Is.False);
        session.CancelListening();
        Assert.That(session.State, Is.EqualTo(AssistantState.Idle));
    }

    [TestCase(ProviderException.Network, "Wi-Fi")]
    [TestCase(ProviderException.Timeout, "too long")]
    [TestCase(ProviderException.RateLimited, "busy")]
    [TestCase(ProviderException.DailyCap, "budget")]
    [TestCase(ProviderException.Misconfigured, "misconfigured")]
    [TestCase(ProviderException.UpstreamError, "went wrong")]
    public void FriendlyError_ExplainsEachFailure(string type, string expected)
    {
        Assert.That(AssistantSession.FriendlyError(new ProviderException(type, "x")), Does.Contain(expected));
    }

    [Test]
    public void Guards()
    {
        Assert.ThrowsAsync<ArgumentException>(() => session.RunTextTurnAsync(" ", null, CancellationToken.None));
        var noStt = new AssistantSession(chat, null, null, registry, new ConversationState(), _ => "", null, () => 0);
        Assert.ThrowsAsync<InvalidOperationException>(() => noStt.RunAudioTurnAsync(Audio(), null, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => new AssistantSession(null, stt, tts, registry, new ConversationState(), _ => "", null, () => 0));
    }
}
