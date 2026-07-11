using PocketMC.Application.Services.Networking;
using PocketMC.Application.Services.Instances;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Networking;
using PocketMC.Application.Services.Shell;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Diagnostics;

/// <summary>
/// Builds a redacted, structured snapshot of PocketMC port diagnostics for support bundles.
/// </summary>
public sealed class PortDiagnosticsSnapshotBuilder
{
    private const string RedactedPublicAddress = "[REDACTED_PUBLIC_ADDRESS]";

    private readonly ApplicationState _appState;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly PortPreflightService _portPreflightService;
    private readonly PortLeaseRegistry _portLeaseRegistry;
    private readonly PortRecoveryService _portRecoveryService;
    private readonly PlayitAgentService _playitAgentService;
    private readonly PlayitApiClient _playitApiClient;
    private readonly DependencyHealthMonitor _dependencyHealthMonitor;
    private readonly ILogger<PortDiagnosticsSnapshotBuilder> _logger;
    private readonly PocketMC.Application.Interfaces.Instances.IGeyserDetector _geyserDetector;
    private readonly ISimpleVoiceChatDetector _voiceChatDetector;

    public PortDiagnosticsSnapshotBuilder(
        ApplicationState appState,
        InstanceRegistry instanceRegistry,
        PortPreflightService portPreflightService,
        PortLeaseRegistry portLeaseRegistry,
        PortRecoveryService portRecoveryService,
        PlayitAgentService playitAgentService,
        PlayitApiClient playitApiClient,
        DependencyHealthMonitor dependencyHealthMonitor,
        ILogger<PortDiagnosticsSnapshotBuilder> logger,
        PocketMC.Application.Interfaces.Instances.IGeyserDetector geyserDetector,
        ISimpleVoiceChatDetector voiceChatDetector)
    {
        _appState = appState;
        _instanceRegistry = instanceRegistry;
        _portPreflightService = portPreflightService;
        _portLeaseRegistry = portLeaseRegistry;
        _portRecoveryService = portRecoveryService;
        _playitAgentService = playitAgentService;
        _playitApiClient = playitApiClient;
        _dependencyHealthMonitor = dependencyHealthMonitor;
        _logger = logger;
        _geyserDetector = geyserDetector;
        _voiceChatDetector = voiceChatDetector;
    }

    /// <summary>
    /// Builds the current port diagnostics snapshot.
    /// </summary>
    public PortDiagnosticsSnapshot Build()
    {
        var instances = _instanceRegistry.GetAll().ToArray();
        var recoveryHistory = _portRecoveryService.GetRecentHistory();

        var snapshot = new PortDiagnosticsSnapshot
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            AppRootState = _appState.IsConfigured ? "Configured" : "NotConfigured",
            InstancePortMappings = instances.Select(BuildInstanceMapping).ToList(),
            LeaseRegistryState = _portLeaseRegistry.GetAllLeases().Select(BuildLease).ToList(),
            RecentPortFailures = recoveryHistory.Select(BuildFailure).ToList(),
            RecoveryHistory = recoveryHistory.Select(BuildRecoveryHistory).ToList(),
            TunnelState = BuildTunnelState(instances),
            PublicConnectivityDependencies = BuildPublicConnectivityDependencies()
        };

        return snapshot;
    }

    private PortDiagnosticsInstanceMapping BuildInstanceMapping(InstanceMetadata metadata)
    {
        string? instancePath = _instanceRegistry.GetPath(metadata.Id);
        PortCheckResult? preflightResult = null;
        IReadOnlyList<PortCheckRequest> requests = Array.Empty<PortCheckRequest>();

        try
        {
            preflightResult = _portPreflightService.Check(metadata, instancePath);
            requests = _portPreflightService.BuildRequests(metadata, instancePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to build port mapping diagnostics for {InstanceName}.", metadata.Name);
        }

        return new PortDiagnosticsInstanceMapping
        {
            InstanceId = metadata.Id,
            InstanceName = metadata.Name,
            ServerType = metadata.ServerType,
            HasGeyser = _geyserDetector.IsGeyserInstalled(instancePath),
            InstancePathPresent = !string.IsNullOrWhiteSpace(instancePath) && Directory.Exists(instancePath),
            CurrentPreflightSuccessful = preflightResult?.IsSuccessful ?? false,
            CurrentPreflightFailureCode = preflightResult?.FailureCode ?? PortFailureCode.UnknownPortFailure,
            CurrentPreflightFailureMessage = preflightResult?.FailureMessage,
            Ports = requests.Select(BuildExpectation).ToList()
        };
    }

    private static PortDiagnosticsExpectation BuildExpectation(PortCheckRequest request)
    {
        return new PortDiagnosticsExpectation
        {
            DisplayName = request.DisplayName,
            BindingRole = request.BindingRole,
            Engine = request.Engine,
            Port = request.Port,
            Protocol = request.Protocol,
            IpMode = request.IpMode,
            BindAddress = request.BindAddress,
            CheckTunnelAvailability = request.CheckTunnelAvailability,
            CheckPublicReachability = request.CheckPublicReachability
        };
    }

    private static PortDiagnosticsLease BuildLease(PortLease lease)
    {
        return new PortDiagnosticsLease
        {
            Port = lease.Port,
            Protocol = lease.Protocol,
            IpMode = lease.IpMode,
            BindAddress = lease.BindAddress,
            InstanceId = lease.InstanceId,
            InstanceName = lease.InstanceName,
            InstancePathPresent = !string.IsNullOrWhiteSpace(lease.InstancePath) && Directory.Exists(lease.InstancePath),
            AcquiredAtUtc = lease.AcquiredAtUtc
        };
    }

    private static PortDiagnosticsFailure BuildFailure(PortRecoveryHistoryEntry entry)
    {
        return new PortDiagnosticsFailure
        {
            OccurredAtUtc = entry.OccurredAtUtc,
            InstanceId = entry.InstanceId,
            InstanceName = entry.InstanceName,
            DisplayName = entry.DisplayName,
            BindingRole = entry.BindingRole,
            Engine = entry.Engine,
            Port = entry.Port,
            Protocol = entry.Protocol,
            IpMode = entry.IpMode,
            BindAddress = entry.BindAddress,
            FailureCode = entry.FailureCode,
            FailureMessage = entry.FailureMessage
        };
    }

    private static PortDiagnosticsRecoveryHistory BuildRecoveryHistory(PortRecoveryHistoryEntry entry)
    {
        return new PortDiagnosticsRecoveryHistory
        {
            OccurredAtUtc = entry.OccurredAtUtc,
            InstanceId = entry.InstanceId,
            InstanceName = entry.InstanceName,
            DisplayName = entry.DisplayName,
            BindingRole = entry.BindingRole,
            Engine = entry.Engine,
            Port = entry.Port,
            Protocol = entry.Protocol,
            IpMode = entry.IpMode,
            BindAddress = entry.BindAddress,
            FailureCode = entry.FailureCode,
            Action = entry.Action,
            IsTransient = entry.IsTransient,
            RetryDelaySeconds = entry.RetryDelay?.TotalSeconds,
            AttemptNumber = entry.AttemptNumber,
            MaxAttempts = entry.MaxAttempts,
            SuggestedPort = entry.SuggestedPort
        };
    }

    private PortDiagnosticsTunnelState BuildTunnelState(IReadOnlyList<InstanceMetadata> instances)
    {
        return new PortDiagnosticsTunnelState
        {
            PlayitAgentState = _playitAgentService.State,
            PlayitAgentRunning = _playitAgentService.IsRunning,
            PlayitBinaryAvailable = _playitAgentService.IsBinaryAvailable,
            PlayitAgentSecretPresent = !string.IsNullOrWhiteSpace(_playitApiClient.GetSecretKey()),
            Instances = instances.Select(BuildInstanceTunnelState).ToList()
        };
    }

    private PortDiagnosticsInstanceTunnelState BuildInstanceTunnelState(InstanceMetadata metadata)
    {
        string? cachedAddress = _appState.GetTunnelAddress(metadata.Id);
        string? cachedVoiceAddress = _appState.GetVoiceChatTunnelAddress(metadata.Id);
        string? instancePath = _instanceRegistry.GetPath(metadata.Id);
        IReadOnlyList<PortCheckRequest> requests;

        try
        {
            requests = _portPreflightService.BuildRequests(metadata, instancePath);
        }
        catch
        {
            requests = Array.Empty<PortCheckRequest>();
        }

        List<string> diagnostics = BuildVoiceChatDiagnostics(requests, instancePath, cachedAddress, cachedVoiceAddress);

        return new PortDiagnosticsInstanceTunnelState
        {
            InstanceId = metadata.Id,
            InstanceName = metadata.Name,
            CachedTunnelAddressPresent = !string.IsNullOrWhiteSpace(cachedAddress),
            CachedTunnelAddress = string.IsNullOrWhiteSpace(cachedAddress) ? null : RedactedPublicAddress,
            CachedVoiceChatTunnelAddressPresent = !string.IsNullOrWhiteSpace(cachedVoiceAddress),
            CachedVoiceChatTunnelAddress = string.IsNullOrWhiteSpace(cachedVoiceAddress) ? null : RedactedPublicAddress,
            ExpectedLocalPorts = requests.Select(x => x.Port).Distinct().OrderBy(x => x).ToList(),
            ExpectedTunnelPorts = requests
                .Where(IsTunnelRelevantRequest)
                .Select(BuildExpectation)
                .ToList(),
            Diagnostics = diagnostics
        };
    }

    private List<string> BuildVoiceChatDiagnostics(
        IReadOnlyList<PortCheckRequest> requests,
        string? instancePath,
        string? javaTunnelAddress,
        string? voiceTunnelAddress)
    {
        var diagnostics = new List<string>();
        PortCheckRequest? voiceRequest = requests.FirstOrDefault(IsSimpleVoiceChatRequest);
        if (voiceRequest == null)
        {
            return diagnostics;
        }

        if (string.IsNullOrWhiteSpace(voiceTunnelAddress))
        {
            diagnostics.Add("Simple Voice Chat UDP tunnel missing");
        }

        SimpleVoiceChatDetection detection = _voiceChatDetector.Detect(instancePath);
        if (detection.IsConfigPending)
        {
            diagnostics.Add("Simple Voice Chat config pending until first run");
        }

        if (detection.IsDetected && string.IsNullOrWhiteSpace(detection.VoiceHost))
        {
            diagnostics.Add("voice_host empty");
        }

        if (!string.IsNullOrWhiteSpace(detection.VoiceHost) &&
            !string.IsNullOrWhiteSpace(javaTunnelAddress) &&
            string.Equals(detection.VoiceHost, javaTunnelAddress, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add("voice_host points to Java tunnel");
        }

        diagnostics.Add($"Windows Firewall: allow inbound UDP {voiceRequest.Port} for Simple Voice Chat.");
        return diagnostics;
    }

    private static bool IsTunnelRelevantRequest(PortCheckRequest request)
    {
        return request.IpMode != PortIpMode.IPv6;
    }

    private static bool IsSimpleVoiceChatRequest(PortCheckRequest request)
    {
        return request.BindingRole == PortBindingRole.SimpleVoiceChat ||
               request.Engine == PortEngine.SimpleVoiceChat;
    }

    private List<PortDiagnosticsDependencyHealth> BuildPublicConnectivityDependencies()
    {
        var health = _dependencyHealthMonitor.GetAllHealth()
            .Where(x => x.Name.Contains("Playit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (health.Count == 0)
        {
            health.Add(_dependencyHealthMonitor.GetHealth("Playit.gg API"));
        }

        return health
            .Select(x => new PortDiagnosticsDependencyHealth
            {
                Name = x.Name,
                Status = x.Status,
                LatencyMilliseconds = x.Latency.TotalMilliseconds,
                LastCheckedUtc = x.LastChecked,
                ErrorMessage = x.ErrorMessage
            })
            .ToList();
    }
}
