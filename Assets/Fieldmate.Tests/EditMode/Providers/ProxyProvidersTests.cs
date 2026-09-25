using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Json;
using Fieldmate.Providers;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Providers;

public class ProxyProvidersTests
{
    [Test]
    public async Task Chat_PostsTheConversation_AndParsesTheAnswer()
    {
        var transport = new FakeTransport().Respond(200,
            "{\"content\":[{\"type\":\"text\",\"text\":\"The relief valve is stuck [fault.overpressure].\"}],\"stop_reason\":\"end_turn\"}");
        var model = new ProxyChatModel(ProxyClientTests.Client(transport));

        var response = await model.CompleteAsync(new ChatRequest("sys", new[] { ChatMessage.User("Why?") }, null), CancellationToken.None);

        Assert.That(response.Text, Does.Contain("[fault.overpressure]"));
        var request = transport.Requests.Single();
        Assert.That(request.Uri.ToString(), Is.EqualTo("https://proxy.test/v1/chat"));
        Assert.That(request.ContentType, Is.EqualTo("application/json"));
        Assert.That(request.TimeoutSeconds, Is.EqualTo(ProxyChatModel.TimeoutSeconds));
        Assert.That(JsonReader.Parse(Encoding.UTF8.GetString(request.Body))["messages"][0]["content"].AsString(), Is.EqualTo("Why?"));
        Assert.That(model.Name, Does.Contain("claude"));
    }

    [Test]
    public void Chat_UnreadableResponse_IsAProviderError()
    {
        var model = new ProxyChatModel(ProxyClientTests.Client(new FakeTransport().Respond(200, "<html>")));

        var e = Assert.ThrowsAsync<ProviderException>(() => model.CompleteAsync(new ChatRequest("", new[] { ChatMessage.User("x") }, null), CancellationToken.None));
        Assert.That(e.ErrorType, Is.EqualTo(ProviderException.InvalidResponse));
        Assert.ThrowsAsync<ArgumentNullException>(() => model.CompleteAsync(null, CancellationToken.None));
    }

    [Test]
    public async Task SpeechToText_SendsWav_WithLanguage_AndReadsTranscript()
    {
        var transport = new FakeTransport().Respond(200, "{\"text\":\"Warum ist der Druck hoch?\",\"confidence\":0.93,\"language\":\"de\"}");
        var stt = new ProxySpeechToText(ProxyClientTests.Client(transport));

        var transcript = await stt.TranscribeAsync(new AudioData(new float[1600], 16000), "de", CancellationToken.None);

        Assert.That(transcript.Text, Is.EqualTo("Warum ist der Druck hoch?"));
        Assert.That(transcript.LanguageCode, Is.EqualTo("de"));
        Assert.That(transcript.Confidence, Is.EqualTo(0.93f).Within(1e-5f));
        var request = transport.Requests.Single();
        Assert.That(request.Uri.ToString(), Is.EqualTo("https://proxy.test/v1/stt?language=de"));
        Assert.That(request.ContentType, Is.EqualTo("audio/wav"));
        Assert.That(Encoding.ASCII.GetString(request.Body, 0, 4), Is.EqualTo("RIFF"));
    }

    [Test]
    public async Task SpeechToText_UnknownLanguage_FallsBackToEnglish()
    {
        var transport = new FakeTransport().Respond(200, "{\"text\":\"hi\"}");
        await new ProxySpeechToText(ProxyClientTests.Client(transport)).TranscribeAsync(new AudioData(new float[10], 16000), "fr", CancellationToken.None);

        Assert.That(transport.Requests.Single().Uri.Query, Is.EqualTo("?language=en"));
    }

    [Test]
    public async Task TextToSpeech_StreamsDecodedSamples_AcrossChunkBoundaries()
    {
        var transport = new FakeTransport().Stream(new byte[] { 0x00, 0x40, 0x00 }, new byte[] { 0xC0 });
        var tts = new ProxyTextToSpeech(ProxyClientTests.Client(transport));
        var samples = new List<float>();

        await tts.StreamAsync("Close the inlet valve.", "de", (buffer, count) => samples.AddRange(buffer.Take(count)), CancellationToken.None);

        Assert.That(samples, Is.EqualTo(new[] { 0.5f, -0.5f }));
        Assert.That(tts.SampleRate, Is.EqualTo(22050));
        var body = JsonReader.Parse(Encoding.UTF8.GetString(transport.Requests.Single().Body));
        Assert.That(body["text"].AsString(), Is.EqualTo("Close the inlet valve."));
        Assert.That(body["language"].AsString(), Is.EqualTo("de"));
    }

    [Test]
    public async Task TextToSpeech_Synthesize_CollectsTheWholeClip()
    {
        var transport = new FakeTransport().Stream(new byte[] { 0x00, 0x40 }, new byte[] { 0x00, 0x40 });
        var audio = await new ProxyTextToSpeech(ProxyClientTests.Client(transport)).SynthesizeAsync("Hi", "en", CancellationToken.None);

        Assert.That(audio.Samples, Is.EqualTo(new[] { 0.5f, 0.5f }));
        Assert.That(audio.SampleRate, Is.EqualTo(22050));
    }

    [Test]
    public async Task TextToSpeech_EmptyText_MakesNoRequest()
    {
        var transport = new FakeTransport();
        await new ProxyTextToSpeech(ProxyClientTests.Client(transport)).StreamAsync("  ", "en", null, CancellationToken.None);

        Assert.That(transport.Requests, Is.Empty);
    }

    [Test]
    public void TextToSpeech_QuotaError_ReachesTheCaller()
    {
        var transport = new FakeTransport().Respond(429, "{\"error\":{\"type\":\"tts_quota\",\"message\":\"Quota used up; answers continue as text.\"}}");

        var e = Assert.ThrowsAsync<ProviderException>(() =>
            new ProxyTextToSpeech(ProxyClientTests.Client(transport)).StreamAsync("Hi", "en", (_, _) => { }, CancellationToken.None));
        Assert.That(e.ErrorType, Is.EqualTo(ProviderException.TtsQuota));
    }

    [Test]
    public void Providers_RequireAClient()
    {
        Assert.Throws<ArgumentNullException>(() => new ProxyChatModel(null));
        Assert.Throws<ArgumentNullException>(() => new ProxySpeechToText(null));
        Assert.Throws<ArgumentNullException>(() => new ProxyTextToSpeech(null));
    }
}

public class AssistantProvidersTests
{
    [Test]
    public void FirstUsableSourceWins()
    {
        var providers = AssistantProviders.FromCandidates(new[]
        {
            ("device keys.json", (string)null),
            ("editor keys.json", "{\"proxyUrl\":\"https://dev.example.com\",\"devToken\":\"t\"}"),
            ("public default", "{\"proxyUrl\":\"https://public.example.com\"}"),
        }, new FakeTransport());

        Assert.That(providers.IsEnabled, Is.True);
        Assert.That(providers.Source, Is.EqualTo("editor keys.json"));
        Assert.That(providers.Config.HasDevToken, Is.True);
        Assert.That(providers.Chat, Is.InstanceOf<ProxyChatModel>());
        Assert.That(providers.SpeechToText, Is.InstanceOf<ProxySpeechToText>());
        Assert.That(providers.TextToSpeech, Is.InstanceOf<ProxyTextToSpeech>());
        Assert.That(providers.DisabledReason, Is.Null);
    }

    [Test]
    public void InvalidSource_FallsThroughToTheNext()
    {
        var providers = AssistantProviders.FromCandidates(new[]
        {
            ("device keys.json", "{\"proxyUrl\":\"http://public.example.com\"}"),
            ("public default", "{\"proxyUrl\":\"https://public.example.com\"}"),
        }, new FakeTransport());

        Assert.That(providers.Source, Is.EqualTo("public default"));
    }

    [Test]
    public void NoUsableSource_DisablesTheAssistant_WithAReason()
    {
        var providers = AssistantProviders.FromCandidates(new[]
        {
            ("device keys.json", "{\"proxyUrl\":\"http://public.example.com\"}"),
            ("public default", "{\"proxyUrl\":\"\"}"),
        }, new FakeTransport());

        Assert.That(providers.IsEnabled, Is.False);
        Assert.That(providers.Chat, Is.Null);
        Assert.That(providers.DisabledReason, Does.StartWith("Assistant offline."));
        Assert.That(providers.DisabledReason, Does.Contain("No proxy URL configured"));
        Assert.That(providers.DisabledReason, Does.Contain("offline fallback"));
    }

    [Test]
    public void NothingConfiguredAtAll_IsDisabled()
    {
        var providers = AssistantProviders.FromCandidates(Array.Empty<(string, string)>(), new FakeTransport());

        Assert.That(providers.IsEnabled, Is.False);
        Assert.That(providers.DisabledReason, Does.Contain("No proxy URL configured"));
    }
}
