using System;
using Fieldmate.AI;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.AI;

public class ProxyConfigTests
{
    [TestCase("{\"proxyUrl\":\"https://fieldmate-proxy.example.workers.dev\"}", "https://fieldmate-proxy.example.workers.dev/")]
    [TestCase("{\"proxyUrl\":\"https://proxy.example.com/base/\"}", "https://proxy.example.com/base/")]
    [TestCase("{\"proxyUrl\":\"http://localhost:8787\"}", "http://localhost:8787/")]
    [TestCase("{\"proxyUrl\":\"http://192.168.1.20:8787\"}", "http://192.168.1.20:8787/")]
    [TestCase("{\"proxyUrl\":\"http://10.0.0.5:8787\"}", "http://10.0.0.5:8787/")]
    [TestCase("{\"proxyUrl\":\"http://172.20.1.1:8787\"}", "http://172.20.1.1:8787/")]
    public void TryParse_AcceptsHttpsAndLocalHttp(string json, string expected)
    {
        Assert.That(ProxyConfig.TryParse(json, out var config, out var reason), Is.True, reason);
        Assert.That(config.BaseUri.ToString(), Is.EqualTo(expected));
        Assert.That(config.HasDevToken, Is.False);
    }

    [TestCase("{}", "No proxy URL")]
    [TestCase("{\"proxyUrl\":\"  \"}", "No proxy URL")]
    [TestCase("{\"proxyUrl\":\"http://proxy.example.com\"}", "must be https")]
    [TestCase("{\"proxyUrl\":\"http://8.8.8.8\"}", "must be https")]
    [TestCase("{\"proxyUrl\":\"ftp://x.example\"}", "must be https")]
    [TestCase("{\"proxyUrl\":\"not a url\"}", "must be https")]
    [TestCase("{proxyUrl}", "not valid JSON")]
    [TestCase(null, "not valid JSON")]
    public void TryParse_RejectsMissingOrUnsafeUrls(string json, string expected)
    {
        Assert.That(ProxyConfig.TryParse(json, out var config, out var reason), Is.False);
        Assert.That(config, Is.Null);
        Assert.That(reason, Does.Contain(expected));
    }

    [Test]
    public void DevToken_AndEndpoints()
    {
        ProxyConfig.TryParse("{\"proxyUrl\":\"https://p.example.com/api\",\"devToken\":\" abc \"}", out var config, out _);

        Assert.That(config.DevToken, Is.EqualTo("abc"));
        Assert.That(config.HasDevToken, Is.True);
        Assert.That(config.Endpoint("/v1/chat").ToString(), Is.EqualTo("https://p.example.com/api/v1/chat"));
        Assert.That(config.Endpoint("v1/stt?language=de").ToString(), Is.EqualTo("https://p.example.com/api/v1/stt?language=de"));
    }
}

public class PcmTests
{
    [Test]
    public void EncodeWav_WritesAHeaderAndClampedSamples()
    {
        var wav = Pcm.EncodeWav(new AudioData(new[] { 0f, 1f, -1f, 2f }, 16000));

        Assert.That(wav, Has.Length.EqualTo(Pcm.WavHeaderBytes + 8));
        Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(System.Text.Encoding.ASCII.GetString(wav, 8, 4), Is.EqualTo("WAVE"));
        Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
        Assert.That(BitConverter.ToInt32(wav, 28), Is.EqualTo(32000));
        Assert.That(BitConverter.ToInt32(wav, 40), Is.EqualTo(8));
        Assert.That(BitConverter.ToInt16(wav, 44), Is.EqualTo(0));
        Assert.That(BitConverter.ToInt16(wav, 46), Is.EqualTo(short.MaxValue));
        Assert.That(BitConverter.ToInt16(wav, 48), Is.EqualTo(-short.MaxValue));
        Assert.That(BitConverter.ToInt16(wav, 50), Is.EqualTo(short.MaxValue), "clamped");
        Assert.Throws<ArgumentNullException>(() => Pcm.EncodeWav(null));
    }

    [Test]
    public void Decoder_HandlesSamplesSplitAcrossChunks()
    {
        var decoder = new Pcm16StreamDecoder();
        var output = new System.Collections.Generic.List<float>();
        void Collect(float[] buffer, int count)
        {
            for (var i = 0; i < count; i++) output.Add(buffer[i]);
        }

        // 0x4000 = 16384 -> 0.5, 0xC000 = -16384 -> -0.5, 0x7FFF -> ~1
        decoder.Decode(new byte[] { 0x00, 0x40, 0x00 }, 3, Collect);
        decoder.Decode(new byte[] { 0xC0 }, 1, Collect);
        decoder.Decode(new byte[] { 0xFF, 0x7F, 0x99 }, 2, Collect);

        Assert.That(output, Is.EqualTo(new[] { 0.5f, -0.5f, 32767f / 32768f }));
        Assert.That(decoder.SamplesDecoded, Is.EqualTo(3));
    }

    [Test]
    public void Decoder_ReusesItsBuffer_AndGrowsWhenNeeded()
    {
        var decoder = new Pcm16StreamDecoder();
        float[] first = null, second = null, big = null;
        decoder.Decode(new byte[8], 8, (b, _) => first = b);
        decoder.Decode(new byte[8], 8, (b, _) => second = b);
        decoder.Decode(new byte[20000], 20000, (b, c) => { big = b; Assert.That(c, Is.EqualTo(10000)); });

        Assert.That(second, Is.SameAs(first));
        Assert.That(big.Length, Is.GreaterThanOrEqualTo(10000));
    }

    [Test]
    public void Decoder_ValidatesArgumentsAndResets()
    {
        var decoder = new Pcm16StreamDecoder();
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode(new byte[2], 3, null));

        decoder.Decode(new byte[] { 1, 2, 3 }, 3, null);
        decoder.Reset();
        var count = -1;
        decoder.Decode(new byte[] { 0, 0x40 }, 2, (_, c) => count = c);
        Assert.That(count, Is.EqualTo(1), "carry cleared by Reset");
        Assert.That(decoder.SamplesDecoded, Is.EqualTo(1));
    }
}

public class ProviderExceptionTests
{
    [TestCase(ProviderException.Network, 0, true)]
    [TestCase(ProviderException.Timeout, 0, true)]
    [TestCase(ProviderException.UpstreamRateLimited, 429, true)]
    [TestCase(ProviderException.UpstreamError, 502, true)]
    [TestCase(ProviderException.UpstreamError, 500, false)]
    [TestCase(ProviderException.DailyCap, 429, false)]
    [TestCase(ProviderException.RateLimited, 429, false)]
    [TestCase(ProviderException.TtsQuota, 429, false)]
    [TestCase(ProviderException.Misconfigured, 500, false)]
    [TestCase(ProviderException.InvalidRequest, 400, false)]
    public void IsTransient_OnlyForNetworkAndGatewayFailures(string type, int status, bool transient)
    {
        Assert.That(new ProviderException(type, "m", status).IsTransient, Is.EqualTo(transient));
    }

    [Test]
    public void EmptyType_DefaultsToUpstreamError()
    {
        Assert.That(new ProviderException(null, "m").ErrorType, Is.EqualTo(ProviderException.UpstreamError));
    }
}
