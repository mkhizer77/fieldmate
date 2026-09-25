using System;

namespace Fieldmate.AI;

/// <summary>
/// A provider call that failed in a way the voice loop can act on: retry later, continue as text, or show that the
/// assistant is misconfigured. <see cref="ErrorType"/> matches the proxy's error types (tools/proxy/README.md).
/// </summary>
public sealed class ProviderException : Exception
{
    public const string Network = "network";
    public const string Timeout = "timeout";
    public const string RateLimited = "rate_limited";
    public const string DailyCap = "daily_cap";
    public const string UpstreamRateLimited = "upstream_rate_limited";
    public const string TtsQuota = "tts_quota";
    public const string Misconfigured = "proxy_misconfigured";
    public const string InvalidRequest = "invalid_request";
    public const string UpstreamError = "upstream_error";
    public const string InvalidResponse = "invalid_response";

    public ProviderException(string errorType, string message, int statusCode = 0, Exception inner = null)
        : base(message, inner)
    {
        ErrorType = string.IsNullOrEmpty(errorType) ? UpstreamError : errorType;
        StatusCode = statusCode;
    }

    public string ErrorType { get; }

    /// <summary>HTTP status, or 0 when the request never got a response.</summary>
    public int StatusCode { get; }

    /// <summary>Worth one more attempt: the network failed or the upstream was briefly unavailable.</summary>
    public bool IsTransient => ErrorType is Network or Timeout or UpstreamRateLimited ||
                               (ErrorType == UpstreamError && StatusCode is 0 or 502 or 503 or 504);
}
