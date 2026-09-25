using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Knowledge;
using Fieldmate.Procedures;

namespace Fieldmate.Tests.EditMode.AI;

internal sealed class ScriptedChat : IChatModel
{
    private readonly Queue<Func<ChatRequest, ChatResponse>> replies = new();
    public List<ChatRequest> Requests { get; } = new();
    public string Name => "scripted";

    public ScriptedChat Say(string text) => Then(_ => new ChatResponse(text, null, StopReason.EndTurn));

    public ScriptedChat Call(string id, string tool, string args, string text = "") =>
        Then(_ => new ChatResponse(text, new[] { new ToolCall(id, tool, args) }, StopReason.ToolUse));

    public ScriptedChat Throw(string type) => Then(_ => throw new ProviderException(type, "boom"));

    public ScriptedChat Then(Func<ChatRequest, ChatResponse> reply)
    {
        replies.Enqueue(reply);
        return this;
    }

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(replies.Dequeue()(request));
    }
}

internal sealed class FakeStt : ISpeechToText
{
    public string Text = "Why is the pressure high?";
    public ProviderException Error;
    public string LastLanguage;

    public Task<Transcript> TranscribeAsync(AudioData audio, string languageCode, CancellationToken cancellationToken)
    {
        LastLanguage = languageCode;
        if (Error != null) throw Error;
        return Task.FromResult(new Transcript(Text, languageCode));
    }
}

internal sealed class FakeTts : IStreamingTextToSpeech
{
    public ProviderException Error;
    public readonly List<string> Spoken = new();
    public int SampleRate => 22050;

    public async Task StreamAsync(string text, string languageCode, Action<float[], int> onSamples, CancellationToken cancellationToken)
    {
        Spoken.Add(text);
        if (Error != null) throw Error;
        onSamples(new[] { 0.1f, 0.2f }, 2);
        await Task.Yield();
        onSamples(new[] { 0.3f }, 1);
    }

    public Task<AudioData> SynthesizeAsync(string text, string languageCode, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class RecordingSink : IAudioSink
{
    public int SampleRate;
    public int Samples;
    public bool Ended;
    public void Begin(int sampleRate) => SampleRate = sampleRate;
    public void Write(float[] samples, int count) => Samples += count;
    public void End() => Ended = true;
}

internal sealed class FakeScene : IAssistantScene
{
    public readonly HashSet<string> Placed = new() { "relief_valve", "motor" };
    public readonly List<string> Highlighted = new();
    public readonly List<string> Notes = new();
    public ManualSection Shown;
    public int ShownStep;
    public string Identified;
    public double Now { get; set; } = 10;

    public bool TryHighlightPart(string partId)
    {
        if (!Placed.Contains(partId)) return false;
        Highlighted.Add(partId);
        return true;
    }

    public void ShowManualSection(ManualSection section) => Shown = section;
    public void ShowStep(ProcedureDefinition procedure, int stepNumber) => ShownStep = stepNumber;
    public void AddNote(string text) => Notes.Add(text);
    public string IdentifyView() => Identified;
}

/// <summary>A test clock that advances a fixed step every time it is read.</summary>
internal sealed class SteppingClock
{
    private double now;
    public double Step = 0.25;
    public double Read() => now += Step;
}
