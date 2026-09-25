using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Fieldmate.Providers;

/// <summary>
/// <see cref="IHttpTransport"/> over UnityWebRequest (design.md §5.1). The network work runs off the main thread inside
/// Unity; completion is observed on the main thread. Cancellation aborts the request.
/// </summary>
public sealed class UnityHttpTransport : IHttpTransport
{
    public Task<HttpResult> SendAsync(HttpRequestSpec request, CancellationToken cancellationToken) =>
        Send(request, new DownloadHandlerBuffer(), cancellationToken);

    public Task<HttpResult> SendStreamingAsync(HttpRequestSpec request, Action<byte[], int> onChunk, CancellationToken cancellationToken) =>
        Send(request, new StreamingDownloadHandler(onChunk), cancellationToken);

    private static async Task<HttpResult> Send(HttpRequestSpec spec, DownloadHandler download, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new UnityWebRequest(spec.Uri, spec.Method)
        {
            downloadHandler = download,
            timeout = spec.TimeoutSeconds,
        };

        if (download is StreamingDownloadHandler streamingHandler)
        {
            streamingHandler.Owner = request;
        }

        if (spec.Body != null)
        {
            request.uploadHandler = new UploadHandlerRaw(spec.Body) { contentType = spec.ContentType };
        }

        foreach (var header in spec.Headers)
        {
            request.SetRequestHeader(header.Key, header.Value);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        request.SendWebRequest().completed += _ => completion.TrySetResult(true);
        using (cancellationToken.Register(() =>
               {
                   request.Abort();
                   completion.TrySetCanceled(cancellationToken);
               }))
        {
            await completion.Task;
        }

        var status = (int)request.responseCode;
        if (request.result is UnityWebRequest.Result.ConnectionError)
        {
            var timedOut = request.error != null && request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
            return new HttpResult(0, null, timedOut, request.error);
        }

        var body = download is StreamingDownloadHandler streaming ? streaming.ErrorBody : download.data;
        return new HttpResult(status, body);
    }

    /// <summary>Forwards 2xx bytes as they arrive; keeps non-2xx bodies (small JSON errors) for the result.</summary>
    private sealed class StreamingDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<byte[], int> onChunk;
        private readonly System.IO.MemoryStream errorBody = new();
        private bool? success;

        public StreamingDownloadHandler(Action<byte[], int> onChunk) : base(new byte[8192]) => this.onChunk = onChunk;

        public byte[] ErrorBody => errorBody.ToArray();

        /// <summary>The request this handler downloads for; its status code is known before the first data arrives.</summary>
        public UnityWebRequest Owner { get; set; }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
            {
                return true;
            }

            success ??= Owner != null && Owner.responseCode >= 200 && Owner.responseCode < 300;

            if (success.Value)
            {
                onChunk?.Invoke(data, dataLength);
            }
            else
            {
                errorBody.Write(data, 0, dataLength);
            }

            return true;
        }
    }
}
