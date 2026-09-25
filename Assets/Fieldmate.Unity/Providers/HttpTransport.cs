using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fieldmate.Providers;

/// <summary>One HTTP request. Kept tiny so providers can be tested with a fake transport instead of the network.</summary>
public sealed class HttpRequestSpec
{
    public HttpRequestSpec(string method, Uri uri, byte[] body, string contentType, int timeoutSeconds)
    {
        Method = method;
        Uri = uri;
        Body = body;
        ContentType = contentType;
        TimeoutSeconds = timeoutSeconds;
    }

    public string Method { get; }
    public Uri Uri { get; }
    public byte[] Body { get; }
    public string ContentType { get; }
    public int TimeoutSeconds { get; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Status 0 means no response (network failure or timeout, see <see cref="TimedOut"/>).</summary>
public sealed class HttpResult
{
    public HttpResult(int status, byte[] body, bool timedOut = false, string networkError = null)
    {
        Status = status;
        Body = body ?? Array.Empty<byte>();
        TimedOut = timedOut;
        NetworkError = networkError;
    }

    public int Status { get; }
    public byte[] Body { get; }
    public bool TimedOut { get; }
    public string NetworkError { get; }
    public bool IsSuccess => Status >= 200 && Status < 300;
}

public interface IHttpTransport
{
    /// <summary>Sends the request. Cancellation aborts it and throws <see cref="OperationCanceledException"/>.</summary>
    Task<HttpResult> SendAsync(HttpRequestSpec request, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="SendAsync"/>, but successful response bytes go to <paramref name="onChunk"/> as they arrive
    /// (the buffer may be reused). Error bodies are returned in the result instead.
    /// </summary>
    Task<HttpResult> SendStreamingAsync(HttpRequestSpec request, Action<byte[], int> onChunk, CancellationToken cancellationToken);
}
