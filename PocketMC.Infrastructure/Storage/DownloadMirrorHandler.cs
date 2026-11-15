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
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan MaxRetryAfterDelay = TimeSpan.FromSeconds(5);
    private const int MaxMirrorAttempts = 3;

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

        // Worst case is one timeout per mirror; cap both the attempt count and the total wall clock.
        DateTime deadline = DateTime.UtcNow + TotalBudget;
        int mirrorAttempts = 0;
        Exception? lastError = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            DownloadMirrorCandidate candidate = candidates[i];
            bool isDirect = ReferenceEquals(candidate.Uri, original) || candidate.Uri == original;
            bool isLast = i == candidates.Count - 1;

            if (!isDirect && mirrorAttempts >= MaxMirrorAttempts)
            {
                _logger.LogInformation(
                    "Giving up on mirrors for {Url} after {Attempts} attempts; requesting the origin directly.",
                    original,
                    mirrorAttempts);
                break;
            }

            if (!isDirect)
            {
                mirrorAttempts++;
            }

            HttpResponseMessage? response = null;
            try
            {
                response = await SendOnceAsync(request, candidate, deadline, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (!isDirect)
                    {
                        _mirrors.ReportSuccess(candidate.Tunnel);
                    }

                    return response;
                }

                // A direct 404 means the artifact is gone. A mirror 404 only means this mirror lacks
                // the path, so keep falling back until the origin itself answers.
                if (response.StatusCode == HttpStatusCode.NotFound && (isDirect || isLast))
                {
                    return response;
                }

                if (isLast)
                {
                    return response;
                }

                _mirrors.ReportFailure(candidate.Tunnel);
                _logger.LogDebug(
                    "Download mirror {Mirror} returned {Status}; trying next candidate.",
                    candidate.Uri.Host,
                    (int)response.StatusCode);
                await DelayForRetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
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

        // Either the mirror budget ran out or every candidate failed; the origin is the last honest answer.
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            throw new HttpRequestException($"All download mirror candidates failed for {original}.", lastError ?? ex);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpRequestMessage request,
        DownloadMirrorCandidate candidate,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        bool isDirect = candidate.Uri == request.RequestUri;
        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (!isDirect && remaining <= TimeSpan.Zero)
        {
            throw new HttpRequestException("Mirror attempt budget exhausted.");
        }

        HttpRequestMessage attempt = isDirect ? request : CloneRequest(request, candidate.Uri);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan attemptTimeout = isDirect ? DirectResponseTimeout : MirrorResponseTimeout;
        if (!isDirect && remaining < attemptTimeout)
        {
            attemptTimeout = remaining;
        }

        timeoutCts.CancelAfter(attemptTimeout);

        try
        {
            // Response headers are enough to decide; the caller streams the body with its own token.
            HttpResponseMessage response = await base.SendAsync(attempt, timeoutCts.Token).ConfigureAwait(false);
            timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
            if (!isDirect)
            {
                // The response owns the headers it copied; report the caller's request for diagnostics.
                response.RequestMessage = request;
            }

            return response;
        }
        finally
        {
            if (!isDirect)
            {
                // GET has no content to stream, so the clone can be released as soon as headers arrive.
                attempt.Dispose();
            }
        }
    }

    private static async Task DelayForRetryAfterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) ||
            response.Headers.RetryAfter is not { } retryAfter)
        {
            return;
        }

        TimeSpan delay = retryAfter.Delta
            ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : TimeSpan.Zero);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        // Keep the UI responsive: never sit on a header-driven backoff longer than a few seconds.
        await Task.Delay(delay > MaxRetryAfterDelay ? MaxRetryAfterDelay : delay, cancellationToken).ConfigureAwait(false);
    }

    private static readonly string[] ForwardedHeaders = { "Range", "Accept", "Accept-Encoding" };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, Uri uri)
    {
        var clone = new HttpRequestMessage(request.Method, uri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        // Mirrors are third parties: forward only the headers a download actually needs.
        foreach (string name in ForwardedHeaders)
        {
            if (request.Headers.TryGetValues(name, out IEnumerable<string>? values))
            {
                clone.Headers.TryAddWithoutValidation(name, values);
            }
        }

        return clone;
    }
}
