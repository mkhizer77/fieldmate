using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fieldmate.AI;
using Fieldmate.Json;

namespace Fieldmate.Providers;

/// <summary>
/// Shared plumbing for the proxy providers: endpoint URLs, the dev-token header, timeouts, one retry for transient
/// failures, and mapping proxy errors (<c>{ "error": { "type", "message" } }</c>) to <see cref="ProviderException"/>.
/// </summary>
public sealed class ProxyClient
{
    public const string DevTokenHeader = "x-fieldmate-dev-token";

    private readonly IHttpTransport transport;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public ProxyClient(ProxyConfig config, IHttpTransport transport, Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public ProxyConfig Config { get; }

    /// <summary>Pause before the single retry.</summary>
    public static TimeSpan RetryDelay { get; } = TimeSpan.FromMilliseconds(400);

    public HttpRequestSpec Post(string path, byte[] body, string contentType, int timeoutSeconds)
    {
        var spec = new HttpRequestSpec("POST", Config.Endpoint(path), body, contentType, timeoutSeconds);
        if (Config.HasDevToken)
        {
            spec.Headers[DevTokenHeader] = Config.DevToken;
        }

        return spec;
    }

    public static byte[] Json(JsonValue value) => Encoding.UTF8.GetBytes(value.ToJson());

    /// <summary>Sends with one retry on transient failure; returns the successful body or throws.</summary>
    public Task<byte[]> SendAsync(HttpRequestSpec spec, CancellationToken cancellationToken) =>
        WithRetry(async () => Check(await transport.SendAsync(spec, cancellationToken)).Body, cancellationToken);

    /// <summary>Streams the successful response to <paramref name="onChunk"/>. Retries only if nothing was streamed yet.</summary>
    public Task SendStreamingAsync(HttpRequestSpec spec, Action<byte[], int> onChunk, CancellationToken cancellationToken)
    {
        var started = false;
        void Forward(byte[] data, int count)
        {
            started = true;
            onChunk(data, count);
        }

        return WithRetry(async () =>
        {
            Check(await transport.SendStreamingAsync(spec, Forward, cancellationToken));
            return true;
        }, cancellationToken, () => !started);
    }

    private async Task<T> WithRetry<T>(Func<Task<T>> attempt, CancellationToken cancellationToken, Func<bool> canRetry = null)
    {
        try
        {
            return await attempt();
        }
        catch (ProviderException e) when (e.IsTransient && (canRetry?.Invoke() ?? true))
        {
            await delay(RetryDelay, cancellationToken);
            return await attempt();
        }
    }

    /// <summary>Maps a transport result to success or a <see cref="ProviderException"/>.</summary>
    public static HttpResult Check(HttpResult result)
    {
        if (result.IsSuccess)
        {
            return result;
        }

        if (result.Status == 0)
        {
            throw result.TimedOut
                ? new ProviderException(ProviderException.Timeout, "The assistant took too long to respond.")
                : new ProviderException(ProviderException.Network, $"Can't reach the assistant ({result.NetworkError ?? "no connection"}).");
        }

        var text = Encoding.UTF8.GetString(result.Body);
        var type = ProviderException.UpstreamError;
        var message = $"Assistant service error ({result.Status}).";
        if (JsonReader.TryParse(text, out var json, out _) && json["error"].Kind == JsonKind.Object)
        {
            type = json["error"]["type"].AsString(type);
            message = json["error"]["message"].AsString(message);
        }

        throw new ProviderException(type, message, result.Status);
    }
}
