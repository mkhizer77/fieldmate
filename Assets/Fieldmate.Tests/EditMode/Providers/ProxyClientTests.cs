using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Providers;
using NUnit.Framework;

namespace Fieldmate.Tests.EditMode.Providers;

public class ProxyClientTests
{
    internal static ProxyConfig Config(string token = null)
    {
        var json = token == null
            ? "{\"proxyUrl\":\"https://proxy.test\"}"
            : $"{{\"proxyUrl\":\"https://proxy.test\",\"devToken\":\"{token}\"}}";
        ProxyConfig.TryParse(json, out var config, out _);
        return config;
    }

    internal static ProxyClient Client(FakeTransport transport, string token = null) =>
        new(Config(token), transport, (_, _) => Task.CompletedTask);

    [Test]
    public void Post_BuildsEndpointHeadersAndTimeout()
    {
        var spec = Client(new FakeTransport(), "tok").Post("v1/chat", new byte[] { 1 }, "application/json", 20);

        Assert.That(spec.Method, Is.EqualTo("POST"));
        Assert.That(spec.Uri.ToString(), Is.EqualTo("https://proxy.test/v1/chat"));
        Assert.That(spec.Headers[ProxyClient.DevTokenHeader], Is.EqualTo("tok"));
        Assert.That(spec.TimeoutSeconds, Is.EqualTo(20));
        Assert.That(Client(new FakeTransport()).Post("v1/chat", null, "x", 1).Headers.ContainsKey(ProxyClient.DevTokenHeader), Is.False);
    }

    [Test]
    public async Task SendAsync_ReturnsBodyOnSuccess()
    {
        var body = await Client(new FakeTransport().Respond(200, "ok")).SendAsync(Spec(), CancellationToken.None);

        Assert.That(Encoding.UTF8.GetString(body), Is.EqualTo("ok"));
    }

    [Test]
    public async Task RetriesOnce_OnNetworkFailure()
    {
        var transport = new FakeTransport().Fail().Respond(200, "ok");
        var body = await Client(transport).SendAsync(Spec(), CancellationToken.None);

        Assert.That(Encoding.UTF8.GetString(body), Is.EqualTo("ok"));
        Assert.That(transport.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void GivesUp_AfterOneRetry()
    {
        var transport = new FakeTransport().Fail(timedOut: true).Fail(timedOut: true);

        var e = Assert.ThrowsAsync<ProviderException>(() => Client(transport).SendAsync(Spec(), CancellationToken.None));
        Assert.That(e.ErrorType, Is.EqualTo(ProviderException.Timeout));
        Assert.That(transport.Requests, Has.Count.EqualTo(2));
    }

    [TestCase(429, "{\"error\":{\"type\":\"daily_cap\",\"message\":\"Budget used up.\"}}", "daily_cap", "Budget used up.")]
    [TestCase(500, "{\"error\":{\"type\":\"proxy_misconfigured\",\"message\":\"Key rejected.\"}}", "proxy_misconfigured", "Key rejected.")]
    [TestCase(404, "<html>not json</html>", "upstream_error", "Assistant service error (404).")]
    public void NonTransientErrors_AreNotRetried(int status, string body, string type, string message)
    {
        var transport = new FakeTransport().Respond(status, body);

        var e = Assert.ThrowsAsync<ProviderException>(() => Client(transport).SendAsync(Spec(), CancellationToken.None));
        Assert.That((e.ErrorType, e.Message, e.StatusCode), Is.EqualTo((type, message, status)));
        Assert.That(transport.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UpstreamBusy_IsRetried()
    {
        var transport = new FakeTransport()
            .Respond(429, "{\"error\":{\"type\":\"upstream_rate_limited\",\"message\":\"busy\"}}")
            .Respond(200, "ok");

        await Client(transport).SendAsync(Spec(), CancellationToken.None);
        Assert.That(transport.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void Streaming_DoesNotRetryAfterDataArrived()
    {
        var transport = new FakeTransport().Enqueue((_, onChunk) =>
        {
            onChunk(new byte[] { 1, 2 }, 2);
            return new HttpResult(0, null, false, "dropped");
        });

        var chunks = 0;
        Assert.ThrowsAsync<ProviderException>(() => Client(transport).SendStreamingAsync(Spec(), (_, _) => chunks++, CancellationToken.None));
        Assert.That(chunks, Is.EqualTo(1));
        Assert.That(transport.Requests, Has.Count.EqualTo(1), "replaying would duplicate audio");
    }

    [Test]
    public void Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => Client(new FakeTransport()).SendAsync(Spec(), cts.Token));
    }

    [Test]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ProxyClient(null, new FakeTransport()));
        Assert.Throws<ArgumentNullException>(() => new ProxyClient(Config(), null));
    }

    private static HttpRequestSpec Spec() => new("POST", new Uri("https://proxy.test/v1/chat"), null, "application/json", 5);
}
