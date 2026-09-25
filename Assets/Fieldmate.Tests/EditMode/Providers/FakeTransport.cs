using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.Providers;

namespace Fieldmate.Tests.EditMode.Providers;

/// <summary>Scripted transport: each call pops the next response (or throws), recording the requests.</summary>
internal sealed class FakeTransport : IHttpTransport
{
    private readonly Queue<Func<HttpRequestSpec, Action<byte[], int>, HttpResult>> responses = new();

    public List<HttpRequestSpec> Requests { get; } = new();

    public FakeTransport Respond(int status, string body) => Enqueue((_, _) => new HttpResult(status, Encoding.UTF8.GetBytes(body)));

    public FakeTransport Fail(bool timedOut = false) => Enqueue((_, _) => new HttpResult(0, null, timedOut, "offline"));

    /// <summary>Streams the chunks, then succeeds.</summary>
    public FakeTransport Stream(params byte[][] chunks) => Enqueue((_, onChunk) =>
    {
        foreach (var chunk in chunks)
        {
            onChunk(chunk, chunk.Length);
        }

        return new HttpResult(200, null);
    });

    public FakeTransport Enqueue(Func<HttpRequestSpec, Action<byte[], int>, HttpResult> response)
    {
        responses.Enqueue(response);
        return this;
    }

    public Task<HttpResult> SendAsync(HttpRequestSpec request, CancellationToken cancellationToken) =>
        SendStreamingAsync(request, (_, _) => throw new InvalidOperationException("not a streaming call"), cancellationToken);

    public Task<HttpResult> SendStreamingAsync(HttpRequestSpec request, Action<byte[], int> onChunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        if (responses.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request to {request.Uri}");
        }

        return Task.FromResult(responses.Dequeue()(request, onChunk));
    }
}
