using System;
using Fieldmate.Json;

namespace Fieldmate.AI;

/// <summary>
/// Where the assistant's providers live (ADR-003): the key-holding proxy URL and, on the owner's headset only, a dev
/// token that lifts the proxy's public caps. Parsed from keys.json; no provider keys ever reach the app.
/// </summary>
public sealed class ProxyConfig
{
    private ProxyConfig(Uri baseUri, string devToken)
    {
        BaseUri = baseUri;
        DevToken = devToken;
    }

    public Uri BaseUri { get; }

    /// <summary>Sent as x-fieldmate-dev-token; null for public installs.</summary>
    public string DevToken { get; }

    public bool HasDevToken => !string.IsNullOrEmpty(DevToken);

    public Uri Endpoint(string path) => new(BaseUri, path.TrimStart('/'));

    /// <summary>
    /// Parses keys.json: <c>{ "proxyUrl": "https://…", "devToken": "…" }</c>. Returns false with a reason when the URL is
    /// missing or unsafe. Plain http is accepted only for localhost and private LAN addresses (wrangler dev).
    /// </summary>
    public static bool TryParse(string json, out ProxyConfig config, out string reason)
    {
        config = null;
        if (!JsonReader.TryParse(json ?? string.Empty, out var root, out var error))
        {
            reason = $"keys.json is not valid JSON ({error.Message}).";
            return false;
        }

        var url = root["proxyUrl"].AsString(string.Empty).Trim();
        if (url.Length == 0)
        {
            reason = "No proxy URL configured (keys.json \"proxyUrl\").";
            return false;
        }

        if (!Uri.TryCreate(url.EndsWith("/") ? url : url + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IsLocal(uri.Host))))
        {
            reason = $"Proxy URL '{url}' must be https (http only for localhost or a private LAN address).";
            return false;
        }

        var token = root["devToken"].AsString(string.Empty).Trim();
        config = new ProxyConfig(uri, token.Length == 0 ? null : token);
        reason = null;
        return true;
    }

    private static bool IsLocal(string host)
    {
        if (host is "localhost" or "127.0.0.1")
        {
            return true;
        }

        var parts = host.Split('.');
        if (parts.Length != 4 || !byte.TryParse(parts[0], out var a) || !byte.TryParse(parts[1], out var b))
        {
            return false;
        }

        return a == 10 || (a == 192 && b == 168) || (a == 172 && b >= 16 && b <= 31);
    }
}
