using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fieldmate.AI;

/// <summary>
/// A chat model with tool use (Anthropic, OpenAI, a mock). Gameplay code only sees this interface; vendor SDKs and
/// HTTP live in Fieldmate.Unity/Providers (CLAUDE.md, design.md §5.1).
/// </summary>
public interface IChatModel
{
    string Name { get; }

    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
}

public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioData audio, string languageCode, CancellationToken cancellationToken);
}

public interface ITextToSpeech
{
    Task<AudioData> SynthesizeAsync(string text, string languageCode, CancellationToken cancellationToken);
}

/// <summary>Mono PCM samples in [-1, 1].</summary>
public sealed class AudioData
{
    public AudioData(float[] samples, int sampleRate)
    {
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        SampleRate = sampleRate > 0 ? sampleRate : throw new ArgumentOutOfRangeException(nameof(sampleRate));
    }

    public float[] Samples { get; }
    public int SampleRate { get; }
    public double DurationSeconds => (double)Samples.Length / SampleRate;
}

public sealed class Transcript
{
    public Transcript(string text, string languageCode, float confidence = 1f)
    {
        Text = text ?? string.Empty;
        LanguageCode = languageCode ?? string.Empty;
        Confidence = confidence;
    }

    public string Text { get; }
    public string LanguageCode { get; }

    /// <summary>0–1 where the provider reports it; 1 otherwise.</summary>
    public float Confidence { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
