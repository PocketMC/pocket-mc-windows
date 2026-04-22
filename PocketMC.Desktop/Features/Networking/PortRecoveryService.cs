using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Networking;

/// <summary>
/// Produces bounded, side-effect-free recovery recommendations for port reliability failures.
/// </summary>
public sealed class PortRecoveryService
{
    private const int MinValidPort = 1;
    private const int MaxValidPort = 65535;
    private const int MaxPrivilegedPort = 1023;
    private const int DefaultJavaPort = 25565;
    private const int DefaultBedrockPort = 19132;
    private const int DefaultPortSearchLimit = 256;
    private const int DefaultMaxRetryAttempts = 3;
    private const int MaxHistoryEntries = 100;
    private static readonly TimeSpan BaseBackoffDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoffDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RecentLeaseWindow = TimeSpan.FromSeconds(30);

    private readonly PortProbeService _portProbeService;
    private readonly PortLeaseRegistry _portLeaseRegistry;
    private readonly ILogger<PortRecoveryService> _logger;
    private readonly ConcurrentQueue<PortRecoveryHistoryEntry> _history = new();

    /// <summary>
    /// Initializes a new port recovery service.
    /// </summary>
    public PortRecoveryService(
        PortProbeService portProbeService,
        PortLeaseRegistry portLeaseRegistry,
        ILogger<PortRecoveryService> logger)
    {
        _portProbeService = portProbeService;
        _portLeaseRegistry = portLeaseRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Produces a recovery recommendation for a single failed port check result.
    /// </summary>
    /// <param name="result">The failed port check result.</param>
    /// <param name="attemptNumber">The zero-based failed attempt number.</param>
    /// <param name="allowAutoPortSwitch">Whether the caller explicitly allows automatic port changes.</param>
    /// <param name="maxRetryAttempts">The maximum number of transient retries allowed.</param>
    /// <returns>A structured recovery recommendation.</returns>
    public PortRecoveryRecommendation Recommend(
        PortCheckResult result,
        int attemptNumber = 0,
        bool allowAutoPortSwitch = false,
        int maxRetryAttempts = DefaultMaxRetryAttempts)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateRetryArguments(attemptNumber, maxRetryAttempts);

        if (result.IsSuccessful || result.FailureCode == PortFailureCode.None)
        {
            PortRecoveryRecommendation recommendation = new(
                PortFailureCode.None,
                "No port recovery needed",
                "The port check completed successfully.",
                suggestedPort: result.Request.Port,
                suggestedProtocol: result.Request.Protocol,
                suggestedIpMode: result.Request.IpMode,
                canAutoApply: false,
                requiresUserAction: false,
                action: PortRecoveryAction.None,
                isTransient: false,
                retryDelay: null,
                attemptNumber: attemptNumber,
                maxAttempts: maxRetryAttempts);

            RecordHistory(result, recommendation);
            return recommendation;
        }

        if (ShouldRetrySamePort(result, attemptNumber, maxRetryAttempts))
        {
            PortRecoveryRecommendation recommendation = CreateBackoffRecommendation(result, attemptNumber, maxRetryAttempts);
            RecordHistory(result, recommendation);
            return recommendation;
        }

        PortRecoveryRecommendation finalRecommendation = result.FailureCode switch
        {
            PortFailureCode.InvalidRange => CreatePortChangeRecommendation(
                result,
                "Use a valid server port",
                $"{result.Request.DisplayName} port {result.Request.Port} is outside the valid range {MinValidPort}-{MaxValidPort}.",
                allowAutoPortSwitch,
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.ReservedOrPrivilegedPort => CreatePortChangeRecommendation(
                result,
                "Use a non-privileged server port",
                $"{result.Request.DisplayName} port {result.Request.Port} is in the privileged/reserved range. A higher port is safer on Windows.",
                allowAutoPortSwitch,
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.TcpConflict or
            PortFailureCode.UdpConflict or
            PortFailureCode.InUseByExternalProcess => CreatePortChangeRecommendation(
                result,
                "Choose an available server port",
                $"{result.Request.DisplayName} port {result.Request.Port} is still unavailable after bounded retry.",
                allowAutoPortSwitch,
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.InUseByPocketMcInstance => CreatePocketMcConflictRecommendation(
                result,
                allowAutoPortSwitch,
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.IPv4BindFailure or
            PortFailureCode.IPv6BindFailure => CreateAbortRecommendation(
                result,
                "Fix the configured bind address",
                BuildBindFailureDescription(result),
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.AccessDenied => CreateAbortRecommendation(
                result,
                "Fix port access permissions",
                $"Windows denied access to port {result.Request.Port}. Choose a higher port or run with permissions that allow this binding.",
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.UnsupportedProtocolOrAddressFamily => CreateAbortRecommendation(
                result,
                "Fix protocol or address-family support",
                result.FailureMessage ?? "The requested protocol or IP mode is not supported by the current system or bind address.",
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.TunnelLimitReached => CreateAbortRecommendation(
                result,
                "Free an existing tunnel",
                "The Playit tunnel limit was reached. Remove an unused tunnel or upgrade the tunnel allowance before retrying.",
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.PlayitTokenInvalid => CreateAbortRecommendation(
                result,
                "Reconnect Playit",
                "The Playit token is invalid or expired. Reconnect the Playit agent before starting the public tunnel.",
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.PlayitClaimRequired => CreateAbortRecommendation(
                result,
                "Connect Playit",
                "PocketMC needs a linked Playit agent before it can continue tunnel setup.",
                attemptNumber,
                maxRetryAttempts),

            PortFailureCode.PlayitAgentOffline or
            PortFailureCode.PublicReachabilityFailure or
            PortFailureCode.UnknownSocketFailure or
            PortFailureCode.UnknownPortFailure => CreateAbortRecommendation(
                result,
                "Retry later",
                $"The failure did not clear after {maxRetryAttempts} retry attempt(s). Try again later or choose a different port.",
                attemptNumber,
                maxRetryAttempts),

            _ => CreateAbortRecommendation(
                result,
                "Review the port failure",
                result.FailureMessage ?? "PocketMC could not recover from this port failure automatically.",
                attemptNumber,
                maxRetryAttempts)
        };

        RecordHistory(result, finalRecommendation);
        return finalRecommendation;
    }

    /// <summary>
    /// Produces recovery recommendations for all supplied failed port check results.
    /// </summary>
    /// <param name="results">The failed port check results.</param>
    /// <param name="attemptNumber">The zero-based failed attempt number.</param>
    /// <param name="allowAutoPortSwitch">Whether the caller explicitly allows automatic port changes.</param>
    /// <param name="maxRetryAttempts">The maximum number of transient retries allowed.</param>
    /// <returns>Structured recovery recommendations in input order.</returns>
    public IReadOnlyList<PortRecoveryRecommendation> RecommendMany(
        IEnumerable<PortCheckResult> results,
        int attemptNumber = 0,
        bool allowAutoPortSwitch = false,
        int maxRetryAttempts = DefaultMaxRetryAttempts)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results
            .Select(result => Recommend(result, attemptNumber, allowAutoPortSwitch, maxRetryAttempts))
            .ToArray();
    }

    /// <summary>
    /// Determines whether the result is likely transient before retry bounds are considered.
    /// </summary>
    /// <param name="result">The failed port check result.</param>
    /// <returns><see langword="true"/> when a bounded retry may resolve the issue; otherwise <see langword="false"/>.</returns>
    public bool IsLikelyTransient(PortCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.FailureCode switch
        {
            PortFailureCode.TcpConflict or PortFailureCode.UdpConflict => !HasStableExternalConflict(result),
            PortFailureCode.InUseByPocketMcInstance => HasRecentPocketMcLeaseConflict(result),
            PortFailureCode.IPv4BindFailure or PortFailureCode.IPv6BindFailure => true,
            PortFailureCode.PlayitAgentOffline => true,
            PortFailureCode.PublicReachabilityFailure => true,
            PortFailureCode.UnknownSocketFailure => true,
            PortFailureCode.UnknownPortFailure => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the exponential backoff delay for a retry attempt.
    /// </summary>
    /// <param name="attemptNumber">The zero-based failed attempt number.</param>
    /// <returns>The bounded retry delay.</returns>
    public TimeSpan GetBackoffDelay(int attemptNumber)
    {
        if (attemptNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number cannot be negative.");
        }

        double multiplier = Math.Pow(2, attemptNumber);
        double seconds = Math.Min(BaseBackoffDelay.TotalSeconds * multiplier, MaxBackoffDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Finds the next available non-privileged port for the supplied request using bounded probing.
    /// </summary>
    /// <param name="request">The failed port check request.</param>
    /// <param name="searchLimit">The maximum number of candidate ports to inspect.</param>
    /// <returns>The next available port, or <see langword="null"/> when none was found inside the bounded search.</returns>
    public int? FindNextFreePort(PortCheckRequest request, int searchLimit = DefaultPortSearchLimit)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (searchLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(searchLimit), "Search limit must be positive.");
        }

        foreach (int candidate in EnumerateCandidatePorts(request, searchLimit))
        {
            if (_portLeaseRegistry.FindHolder(candidate, request.Protocol, request.IpMode, request.BindAddress) != null)
            {
                continue;
            }

            var candidateRequest = new PortCheckRequest(
                candidate,
                request.Protocol,
                request.IpMode,
                request.BindAddress,
                request.InstanceId,
                request.InstanceName,
                request.InstancePath,
                request.CheckTunnelAvailability,
                request.CheckPublicReachability,
                request.BindingRole,
                request.Engine,
                request.DisplayName);

            PortCheckResult probeResult = _portProbeService.Probe(candidateRequest);
            if (probeResult.IsSuccessful)
            {
                return candidate;
            }
        }

        _logger.LogDebug(
            "No free port was found near {Port} for {Protocol}/{IpMode} after scanning {SearchLimit} candidates.",
            request.Port,
            request.Protocol,
            request.IpMode,
            searchLimit);
        return null;
    }

    /// <summary>
    /// Returns recent recovery recommendations produced by this service.
    /// </summary>
    /// <param name="maxEntries">The maximum number of newest entries to return.</param>
    /// <returns>A newest-first snapshot of recent recovery history.</returns>
    public IReadOnlyList<PortRecoveryHistoryEntry> GetRecentHistory(int maxEntries = 50)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Maximum entries must be positive.");
        }

        return _history
            .Reverse()
            .Take(maxEntries)
            .ToArray();
    }

    private bool ShouldRetrySamePort(PortCheckResult result, int attemptNumber, int maxRetryAttempts)
    {
        return attemptNumber < maxRetryAttempts && IsLikelyTransient(result);
    }

    private PortRecoveryRecommendation CreateBackoffRecommendation(
        PortCheckResult result,
        int attemptNumber,
        int maxRetryAttempts)
    {
        TimeSpan delay = GetBackoffDelay(attemptNumber);
        return new PortRecoveryRecommendation(
            result.FailureCode,
            "Retry the same port",
            $"This looks transient. Wait {delay.TotalSeconds:0} second(s), then retry port {result.Request.Port} without changing configuration.",
            suggestedPort: result.Request.Port,
            suggestedBindAddress: result.Request.BindAddress,
            suggestedProtocol: result.Request.Protocol,
            suggestedIpMode: result.Request.IpMode,
            canAutoApply: true,
            requiresUserAction: false,
            action: PortRecoveryAction.WaitWithBackoff,
            isTransient: true,
            retryDelay: delay,
            attemptNumber: attemptNumber,
            maxAttempts: maxRetryAttempts);
    }

    private PortRecoveryRecommendation CreatePortChangeRecommendation(
        PortCheckResult result,
        string title,
        string description,
        bool allowAutoPortSwitch,
        int attemptNumber,
        int maxRetryAttempts)
    {
        int? suggestedPort = FindNextFreePort(result.Request);
        if (!suggestedPort.HasValue)
        {
            return CreateAbortRecommendation(
                result,
                title,
                $"{description} PocketMC could not find a free replacement port in the bounded search window.",
                attemptNumber,
                maxRetryAttempts);
        }

        PortRecoveryAction action = allowAutoPortSwitch
            ? PortRecoveryAction.AutoSwitchToNextFreePort
            : PortRecoveryAction.SuggestNextFreePort;

        string actionText = allowAutoPortSwitch
            ? $"PocketMC may switch this instance to port {suggestedPort.Value} because automatic port changes were explicitly allowed."
            : $"Ask the user before changing the configured port to {suggestedPort.Value}.";

        return new PortRecoveryRecommendation(
            result.FailureCode,
            title,
            $"{description} {actionText}",
            suggestedPort: suggestedPort,
            suggestedBindAddress: result.Request.BindAddress,
            suggestedProtocol: result.Request.Protocol,
            suggestedIpMode: result.Request.IpMode,
            canAutoApply: allowAutoPortSwitch,
            requiresUserAction: !allowAutoPortSwitch,
            action: action,
            isTransient: false,
            retryDelay: null,
            attemptNumber: attemptNumber,
            maxAttempts: maxRetryAttempts);
    }

    private PortRecoveryRecommendation CreatePocketMcConflictRecommendation(
        PortCheckResult result,
        bool allowAutoPortSwitch,
        int attemptNumber,
        int maxRetryAttempts)
    {
        PortConflictInfo? conflict = result.Conflicts.FirstOrDefault(x => x.ExistingLease != null);
        string owner = conflict?.ExistingLease?.InstanceName ?? "another PocketMC instance";

        return CreatePortChangeRecommendation(
            result,
            "Resolve the PocketMC port conflict",
            $"{result.Request.DisplayName} port {result.Request.Port} is already assigned to {owner}. Stop the other instance or use a different port.",
            allowAutoPortSwitch,
            attemptNumber,
            maxRetryAttempts);
    }

    private static PortRecoveryRecommendation CreateAbortRecommendation(
        PortCheckResult result,
        string title,
        string description,
        int attemptNumber,
        int maxRetryAttempts)
    {
        return new PortRecoveryRecommendation(
            result.FailureCode,
            title,
            description,
            suggestedPort: null,
            suggestedBindAddress: result.Request.BindAddress,
            suggestedProtocol: result.Request.Protocol,
            suggestedIpMode: result.Request.IpMode,
            canAutoApply: false,
            requiresUserAction: true,
            action: PortRecoveryAction.Abort,
            isTransient: false,
            retryDelay: null,
            attemptNumber: attemptNumber,
            maxAttempts: maxRetryAttempts);
    }

    private static string BuildBindFailureDescription(PortCheckResult result)
    {
        string ipMode = result.FailureCode == PortFailureCode.IPv6BindFailure ? "IPv6" : "IPv4";
        if (!string.IsNullOrWhiteSpace(result.Request.BindAddress))
        {
            return $"PocketMC could not bind {ipMode} on '{result.Request.BindAddress}:{result.Request.Port}'. Check the configured server IP or clear the bind address.";
        }

        return $"PocketMC could not bind {ipMode} on port {result.Request.Port}. Check whether the local network stack supports this binding.";
    }

    private static void ValidateRetryArguments(int attemptNumber, int maxRetryAttempts)
    {
        if (attemptNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number cannot be negative.");
        }

        if (maxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryAttempts), "Maximum retry attempts cannot be negative.");
        }
    }

    private static bool HasStableExternalConflict(PortCheckResult result)
    {
        return result.Conflicts.Any(conflict =>
            conflict.FailureCode == PortFailureCode.InUseByExternalProcess ||
            conflict.IsExternalProcessConflict);
    }

    private static bool HasRecentPocketMcLeaseConflict(PortCheckResult result)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return result.Conflicts.Any(conflict =>
            conflict.ExistingLease != null &&
            conflict.ExistingLease.AcquiredAtUtc != DateTimeOffset.MinValue &&
            now - conflict.ExistingLease.AcquiredAtUtc <= RecentLeaseWindow);
    }

    private static IEnumerable<int> EnumerateCandidatePorts(PortCheckRequest request, int searchLimit)
    {
        int defaultPort = request.Protocol == PortProtocol.Udp ? DefaultBedrockPort : DefaultJavaPort;
        int startPort = IsValidPort(request.Port)
            ? Math.Min(request.Port + 1, MaxValidPort)
            : defaultPort;

        var yielded = new HashSet<int>();
        foreach (int candidate in EnumerateCandidateRange(startPort, searchLimit, yielded))
        {
            yield return candidate;
        }

        foreach (int candidate in EnumerateCandidateRange(defaultPort, searchLimit - yielded.Count, yielded))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<int> EnumerateCandidateRange(int startPort, int remainingLimit, HashSet<int> yielded)
    {
        if (remainingLimit <= 0)
        {
            yield break;
        }

        int checkedCount = 0;
        for (int port = Math.Max(startPort, MinValidPort); port <= MaxValidPort && checkedCount < remainingLimit; port++)
        {
            checkedCount++;
            if (port <= MaxPrivilegedPort || !yielded.Add(port))
            {
                continue;
            }

            yield return port;
        }
    }

    private static bool IsValidPort(int port) => port >= MinValidPort && port <= MaxValidPort;

    private void RecordHistory(PortCheckResult result, PortRecoveryRecommendation recommendation)
    {
        if (result.FailureCode == PortFailureCode.None)
        {
            return;
        }

        _history.Enqueue(
            new PortRecoveryHistoryEntry(
                DateTimeOffset.UtcNow,
                result.Request.InstanceId,
                result.Request.InstanceName,
                result.Request.Port,
                result.Request.Protocol,
                result.Request.IpMode,
                result.Request.BindAddress,
                result.FailureCode,
                result.FailureMessage,
                recommendation.Action,
                recommendation.IsTransient,
                recommendation.RetryDelay,
                recommendation.AttemptNumber,
                recommendation.MaxAttempts,
                recommendation.SuggestedPort,
                result.Request.BindingRole,
                result.Request.Engine,
                result.Request.DisplayName));

        while (_history.Count > MaxHistoryEntries && _history.TryDequeue(out _))
        {
        }
    }
}
