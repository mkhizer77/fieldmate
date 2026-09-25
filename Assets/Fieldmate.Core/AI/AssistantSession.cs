using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.Json;

namespace Fieldmate.AI;

/// <summary>States shown to the user (design.md §5.4: listening / thinking / speaking indicator).</summary>
public enum AssistantState
{
    Idle,
    Listening,
    Transcribing,
    Thinking,
    Speaking,
}

public enum TranscriptKind
{
    User,
    Assistant,
    Tool,
    Info,
    Error,
}

public readonly struct TranscriptEntry
{
    public TranscriptEntry(TranscriptKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public TranscriptKind Kind { get; }
    public string Text { get; }

    public override string ToString() => $"{Kind}: {Text}";
}

/// <summary>Where streamed speech goes (a Unity AudioSource in the app, a buffer in tests).</summary>
public interface IAudioSink
{
    void Begin(int sampleRate);
    void Write(float[] samples, int count);
    void End();
}

/// <summary>Seconds per stage of one turn, measured from the end of the user's speech.</summary>
public sealed class TurnTimings
{
    public double SpeechToText { get; internal set; }
    public double Chat { get; internal set; }
    public int ChatRequests { get; internal set; }

    /// <summary>Perceived response time: end of user speech to first audio (or to the text when there is no voice).</summary>
    public double FirstResponse { get; internal set; }

    public double Total { get; internal set; }

    public override string ToString() =>
        $"stt {SpeechToText:0.00}s, chat {Chat:0.00}s ({ChatRequests} req), first response {FirstResponse:0.00}s, total {Total:0.00}s";
}

public sealed class TurnResult
{
    public bool Success { get; internal set; }
    public string UserText { get; internal set; }
    public string AssistantText { get; internal set; }
    public List<string> ToolsUsed { get; } = new();
    public TurnTimings Timings { get; } = new();

    /// <summary><see cref="ProviderException.ErrorType"/> of the failure, if any.</summary>
    public string ErrorType { get; internal set; }
}

/// <summary>
/// One assistant conversation (design.md §5.4): audio → speech-to-text → chat with tool rounds → speech. Tools run
/// through the <see cref="ToolRegistry"/> and are echoed in the transcript. A failed turn is rolled back so the
/// conversation stays valid; <see cref="ConsecutiveFailures"/> drives the offline fallback (#16).
/// </summary>
public sealed class AssistantSession
{
    public const int MaxToolRounds = 3;

    private readonly IChatModel chat;
    private readonly ISpeechToText speechToText;
    private readonly ITextToSpeech textToSpeech;
    private readonly ToolRegistry tools;
    private readonly Func<string, string> systemPrompt;
    private readonly Func<string, string> context;
    private readonly Func<double> clock;

    public AssistantSession(IChatModel chat, ISpeechToText speechToText, ITextToSpeech textToSpeech, ToolRegistry tools,
        ConversationState conversation, Func<string, string> systemPrompt, Func<string, string> context, Func<double> clock)
    {
        this.chat = chat ?? throw new ArgumentNullException(nameof(chat));
        this.speechToText = speechToText;
        this.textToSpeech = textToSpeech;
        this.tools = tools ?? throw new ArgumentNullException(nameof(tools));
        Conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        this.systemPrompt = systemPrompt ?? throw new ArgumentNullException(nameof(systemPrompt));
        this.context = context ?? (_ => string.Empty);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public event Action<AssistantState> StateChanged;
    public event Action<TranscriptEntry> TranscriptAdded;

    public ConversationState Conversation { get; }
    public AssistantState State { get; private set; }

    /// <summary>"en" or "de"; changed by the set_language tool via <see cref="AssistantTools.LanguageChanged"/>.</summary>
    public string Language { get; set; } = "en";

    public int ConsecutiveFailures { get; private set; }
    public TurnResult LastTurn { get; private set; }

    /// <summary>Push-to-talk pressed. Ignored unless idle.</summary>
    public bool BeginListening()
    {
        if (State != AssistantState.Idle)
        {
            return false;
        }

        SetState(AssistantState.Listening);
        return true;
    }

    /// <summary>Push-to-talk released without usable audio (too short, cancelled).</summary>
    public void CancelListening()
    {
        if (State == AssistantState.Listening)
        {
            SetState(AssistantState.Idle);
        }
    }

    /// <summary>Transcribes the recording, then runs the turn.</summary>
    public async Task<TurnResult> RunAudioTurnAsync(AudioData audio, IAudioSink sink, CancellationToken cancellationToken)
    {
        if (speechToText == null)
        {
            throw new InvalidOperationException("No speech-to-text provider.");
        }

        var result = new TurnResult();
        var start = clock();
        SetState(AssistantState.Transcribing);
        string text;
        try
        {
            var transcript = await speechToText.TranscribeAsync(audio, Language, cancellationToken);
            text = transcript.Text.Trim();
        }
        catch (ProviderException e)
        {
            return Fail(result, e, start);
        }
        catch (OperationCanceledException)
        {
            SetState(AssistantState.Idle);
            throw;
        }

        result.Timings.SpeechToText = clock() - start;
        if (text.Length == 0)
        {
            Add(TranscriptKind.Info, Language == "de" ? "Nicht verstanden. Bitte noch einmal." : "I didn't catch that. Please try again.");
            SetState(AssistantState.Idle);
            result.Timings.Total = clock() - start;
            return LastTurn = result;
        }

        return await RunTurnAsync(text, sink, result, start, cancellationToken);
    }

    /// <summary>Runs a turn from typed or scripted text (debug panel, AI eval).</summary>
    public Task<TurnResult> RunTextTurnAsync(string text, IAudioSink sink, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        return RunTurnAsync(text.Trim(), sink, new TurnResult(), clock(), cancellationToken);
    }

    private async Task<TurnResult> RunTurnAsync(string text, IAudioSink sink, TurnResult result, double start, CancellationToken cancellationToken)
    {
        result.UserText = text;
        Add(TranscriptKind.User, text);
        SetState(AssistantState.Thinking);

        var rollback = Conversation.Messages.Count;
        Conversation.AddUser(text);
        var turnContext = context(text);
        var speech = new SpeechChannel(this, sink, result, start, cancellationToken);
        try
        {
            ChatResponse response;
            try
            {
                response = await CompleteWithToolsAsync(turnContext, result, speech, cancellationToken);
            }
            catch (ProviderException e)
            {
                Conversation.RollbackTo(rollback);
                return Fail(result, e, start);
            }
            catch (OperationCanceledException)
            {
                Conversation.RollbackTo(rollback);
                SetState(AssistantState.Idle);
                throw;
            }

            var answer = response.Text.Trim();
            result.AssistantText = answer;
            if (answer.Length > 0)
            {
                Add(TranscriptKind.Assistant, answer);
                speech.Enqueue(answer);
            }
        }
        finally
        {
            await speech.FinishAsync();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            SetState(AssistantState.Idle); // barge-in while speaking
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.Timings.FirstResponse <= 0)
        {
            result.Timings.FirstResponse = clock() - start;
        }

        ConsecutiveFailures = 0;
        result.Success = true;
        result.Timings.Total = clock() - start;
        SetState(AssistantState.Idle);
        return LastTurn = result;
    }

    private async Task<ChatResponse> CompleteWithToolsAsync(string turnContext, TurnResult result, SpeechChannel speech, CancellationToken cancellationToken)
    {
        for (var round = 0; ; round++)
        {
            var requestStart = clock();
            var request = new ChatRequest(systemPrompt(Language), Conversation.Messages, tools.Definitions, turnContext, 300);
            var response = await chat.CompleteAsync(request, cancellationToken);
            result.Timings.Chat += clock() - requestStart;
            result.Timings.ChatRequests++;
            Conversation.AddAssistant(response);

            if (response.ToolCalls.Count == 0)
            {
                return response;
            }

            // "Let me show you…": said while the tools and the follow-up request run, so the user hears a reply early.
            var preamble = response.Text.Trim();
            if (preamble.Length > 0)
            {
                Add(TranscriptKind.Assistant, preamble);
                speech.Enqueue(preamble);
            }

            foreach (var call in response.ToolCalls)
            {
                var toolResult = round < MaxToolRounds
                    ? await tools.ExecuteAsync(call, cancellationToken)
                    : ToolResult.Failure(call, "Tool budget for this turn is used up. Answer the user now.");
                Conversation.AddToolResult(toolResult);
                result.ToolsUsed.Add(call.Name);
                Add(TranscriptKind.Tool, $"{call.Name}({DescribeArguments(call.ArgumentsJson)}) → {toolResult.Content}");
            }

            if (round > MaxToolRounds)
            {
                return response; // hard stop: calls were answered with "budget used up", history stays valid
            }
        }
    }

    /// <summary>
    /// Speech for one turn: utterances play one after another into a single sink stream, started lazily with the first
    /// audio and ended when the turn finishes. A voice failure leaves the text answer standing.
    /// </summary>
    private sealed class SpeechChannel
    {
        private readonly AssistantSession session;
        private readonly IAudioSink sink;
        private readonly TurnResult result;
        private readonly double start;
        private readonly CancellationToken cancellationToken;
        private Task chain = Task.CompletedTask;
        private bool begun;
        private bool reportedFailure;

        public SpeechChannel(AssistantSession session, IAudioSink sink, TurnResult result, double start, CancellationToken cancellationToken)
        {
            this.session = session;
            this.sink = sink;
            this.result = result;
            this.start = start;
            this.cancellationToken = cancellationToken;
        }

        private bool Enabled => session.textToSpeech != null && sink != null;

        public void Enqueue(string text)
        {
            var speech = AssistantPrompt.ForSpeech(text);
            if (!Enabled || speech.Length == 0)
            {
                return;
            }

            var previous = chain;
            chain = SpeakAfter(previous, speech);
        }

        public async Task FinishAsync()
        {
            try
            {
                await chain;
            }
            catch (OperationCanceledException)
            {
                // the turn itself reports cancellation
            }
            finally
            {
                if (begun)
                {
                    sink.End();
                }
            }
        }

        private async Task SpeakAfter(Task previous, string speech)
        {
            await previous;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (session.textToSpeech is IStreamingTextToSpeech streaming)
                {
                    await streaming.StreamAsync(speech, session.Language, (samples, count) =>
                    {
                        EnsureBegun(streaming.SampleRate);
                        sink.Write(samples, count);
                    }, cancellationToken);
                }
                else
                {
                    var audio = await session.textToSpeech.SynthesizeAsync(speech, session.Language, cancellationToken);
                    EnsureBegun(audio.SampleRate);
                    sink.Write(audio.Samples, audio.Samples.Length);
                }
            }
            catch (ProviderException e)
            {
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    session.Add(TranscriptKind.Info, e.ErrorType == ProviderException.TtsQuota
                        ? "Voice unavailable right now; answering in text."
                        : "Couldn't play the voice; the answer is shown above.");
                }
            }
        }

        private void EnsureBegun(int sampleRate)
        {
            if (begun)
            {
                return;
            }

            begun = true;
            if (result.Timings.FirstResponse <= 0)
            {
                result.Timings.FirstResponse = session.clock() - start;
            }

            sink.Begin(sampleRate);
            session.SetState(AssistantState.Speaking);
        }
    }

    private TurnResult Fail(TurnResult result, ProviderException e, double start)
    {
        ConsecutiveFailures++;
        result.ErrorType = e.ErrorType;
        result.Timings.Total = clock() - start;
        Add(TranscriptKind.Error, FriendlyError(e));
        SetState(AssistantState.Idle);
        return LastTurn = result;
    }

    /// <summary>User-facing wording for provider failures.</summary>
    public static string FriendlyError(ProviderException e) => e.ErrorType switch
    {
        ProviderException.Network => "I can't reach the assistant service. Check the Wi-Fi.",
        ProviderException.Timeout => "The assistant took too long. Please try again.",
        ProviderException.RateLimited or ProviderException.UpstreamRateLimited => "The assistant is busy. Try again in a moment.",
        ProviderException.DailyCap => "The demo's daily assistant budget is used up. Try again tomorrow.",
        ProviderException.Misconfigured => "The assistant service is misconfigured.",
        _ => "Something went wrong with the assistant. Please try again.",
    };

    private static string DescribeArguments(string json)
    {
        if (!JsonReader.TryParse(string.IsNullOrWhiteSpace(json) ? "{}" : json, out var value, out _) || value.Members.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(value.Members.Count);
        foreach (var member in value.Members)
        {
            parts.Add(member.Value.Kind == JsonKind.String ? $"{member.Key}={member.Value.AsString()}" : $"{member.Key}={member.Value.ToJson()}");
        }

        return string.Join(", ", parts);
    }

    private void Add(TranscriptKind kind, string text) => TranscriptAdded?.Invoke(new TranscriptEntry(kind, text));

    private void SetState(AssistantState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }
}
