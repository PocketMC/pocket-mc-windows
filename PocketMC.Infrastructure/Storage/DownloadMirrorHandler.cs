using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Storage;

/// <summary>
/// Retries a GET request through mirror candidates when the origin is blocked, times out, or
/// answers with a transient failure. Only GET requests without a body are considered, so
/// uploads keep going straight to the origin.
/// </summary>
public sealed class DownloadMirrorHandler : DelegatingHandler
{
    private static readonly TimeSpan DirectResponseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MirrorResponseTimeout = TimeSpan.FromSeconds(20);

    private readonly DownloadMirrorService _mirrors;
    private readonly ILogger<DownloadMirrorHandler> _logger;

    public DownloadMirrorHandler(DownloadMirrorService mirrors, ILogger<DownloadMirrorHandler> logger)
    {
        _mirrors = mirrors ?? throw new ArgumentNullException(nameof(mirrors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not Uri original)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<DownloadMirrorCandidate> candidates = _mirrors.GetCandidates(original);
        if (candidates.Count == 1)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        Exception? lastError = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            DownloadMirrorCandidate candidate = candidates[i];
            bool isDirect = ReferenceEquals(candidate.Uri, original) || candidate.Uri == original;
            bool isLast = i == candidates.Count - 1;

            HttpResponseMessage? response = null;
            try
            {
                response = await SendOnceAsync(request, candidate, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (!isDirect)
                    {
                        _mirrors.ReportSuccess(candidate.Tunnel);
                    }

                    return response;
                }

                if (response.StatusCode == HttpStatusCode.NotFound || isLast)
                {
                    return response;
                }

                _mirrors.ReportFailure(candidate.Tunnel);
                _logger.LogDebug(
                    "Download mirror {Mirror} returned {Status}; trying next candidate.",
                    candidate.Uri.Host,
                    (int)response.StatusCode);
                response.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                response?.Dispose();
                _mirrors.ReportFailure(candidate.Tunnel);
                lastError = ex;

                if (isLast)
                {
                    throw new HttpRequestException(
                        $"All download mirror candidates failed for {original}.",
                        lastError);
                }

                _logger.LogDebug(
                    "Download candidate {Mirror} failed: {Message}; trying next candidate.",
                    candidate.Uri.Host,
                    ex.Message);
            }
        }

        throw new HttpRequestException($"All download mirror candidates failed for {original}.", lastError);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpRequestMessage request,
        DownloadMirrorCandidate candidate,
        CancellationToken cancellationToken)
    {
        bool isDirect = candidate.Uri == request.RequestUri;
        HttpRequestMessage attempt = isDirect ? request : CloneRequest(request, candidate.Uri);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(isDirect ? DirectResponseTimeout : MirrorResponseTimeout);

        try
        {
            // Response headers are enough to decide; the caller streams the body with its own token.
            HttpResponseMessage response = await base.SendAsync(attempt, timeoutCts.Token).ConfigureAwait(false);
            timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
            return response;
        }
        finally
        {
            if (!isDirect)
            {
                // Response disposal handles the request; drop the clone when no response was produced.
                if (attempt.Content != null)
                {
                    attempt.Content.Dispose();
                }
            }
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, Uri uri)
    {
        var clone = new HttpRequestMessage(request.Method, uri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
