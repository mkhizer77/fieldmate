using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Json;

namespace Fieldmate.Providers;

/// <summary>Claude (via the proxy's /v1/chat). The proxy pins the model; see ADR-003.</summary>
public sealed class ProxyChatModel : IChatModel
{
    public const int TimeoutSeconds = 20;
    private readonly ProxyClient client;

    public ProxyChatModel(ProxyClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public string Name => "claude (proxy)";

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var spec = client.Post("v1/chat", ProxyClient.Json(AnthropicWire.ToRequest(request)), "application/json", TimeoutSeconds);
        var body = await client.SendAsync(spec, cancellationToken);
        if (!JsonReader.TryParse(Encoding.UTF8.GetString(body), out var json, out var error))
        {
            throw new ProviderException(ProviderException.InvalidResponse, $"Unreadable chat response ({error.Message}).");
        }

        return AnthropicWire.FromResponse(json);
    }
}

/// <summary>Claude with an image (via the proxy's /v1/vision): one camera frame per "What's this?".</summary>
public sealed class ProxyVisionModel : IVisionModel
{
    public const int TimeoutSeconds = 8;
    private readonly ProxyClient client;

    public ProxyVisionModel(ProxyClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<string> AskAsync(byte[] image, string mediaType, string prompt, CancellationToken cancellationToken)
    {
        if (image == null || image.Length == 0)
        {
            throw new ArgumentException("No image to send.", nameof(image));
        }

        var request = JsonValue.Object(
            ("image", JsonValue.From(Convert.ToBase64String(image))),
            ("media_type", JsonValue.From(mediaType)),
            ("prompt", JsonValue.From(prompt ?? string.Empty)));
        var body = await client.SendAsync(client.Post("v1/vision", ProxyClient.Json(request), "application/json", TimeoutSeconds), cancellationToken);
        if (!JsonReader.TryParse(Encoding.UTF8.GetString(body), out var json, out _) || !json["text"].TryGetString(out var text))
        {
            throw new ProviderException(ProviderException.InvalidResponse, "Unreadable vision response.");
        }

        return text;
    }
}

/// <summary>Deepgram Nova-3 (via the proxy's /v1/stt). Audio goes up as a 16-bit WAV.</summary>
public sealed class ProxySpeechToText : ISpeechToText
{
    public const int TimeoutSeconds = 10;
    private readonly ProxyClient client;

    public ProxySpeechToText(ProxyClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<Transcript> TranscribeAsync(AudioData audio, string languageCode, CancellationToken cancellationToken)
    {
        var language = languageCode == "de" ? "de" : "en";
        var spec = client.Post($"v1/stt?language={language}", Pcm.EncodeWav(audio), "audio/wav", TimeoutSeconds);
        var body = await client.SendAsync(spec, cancellationToken);
        if (!JsonReader.TryParse(Encoding.UTF8.GetString(body), out var json, out _) || json.Kind != JsonKind.Object)
        {
            throw new ProviderException(ProviderException.InvalidResponse, "Unreadable transcription response.");
        }

        return new Transcript(json["text"].AsString(string.Empty), json["language"].AsString(language),
            (float)json["confidence"].AsNumber(0d));
    }
}

/// <summary>ElevenLabs Flash v2.5 (via the proxy's /v1/tts), streamed as 16-bit mono PCM at 22.05 kHz.</summary>
public sealed class ProxyTextToSpeech : IStreamingTextToSpeech
{
    public const int TimeoutSeconds = 15;
    public const int ProxySampleRate = 22050;
    private readonly ProxyClient client;

    public ProxyTextToSpeech(ProxyClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public int SampleRate => ProxySampleRate;

    public async Task StreamAsync(string text, string languageCode, Action<float[], int> onSamples, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var body = JsonValue.Object(("text", JsonValue.From(text)), ("language", JsonValue.From(languageCode == "de" ? "de" : "en")));
        var decoder = new Pcm16StreamDecoder();
        await client.SendStreamingAsync(client.Post("v1/tts", ProxyClient.Json(body), "application/json", TimeoutSeconds),
            (data, count) => decoder.Decode(data, count, onSamples), cancellationToken);
    }

    public async Task<AudioData> SynthesizeAsync(string text, string languageCode, CancellationToken cancellationToken)
    {
        var samples = new List<float>();
        await StreamAsync(text, languageCode, (buffer, count) =>
        {
            for (var i = 0; i < count; i++)
            {
                samples.Add(buffer[i]);
            }
        }, cancellationToken);
        return new AudioData(samples.ToArray(), SampleRate);
    }
}
