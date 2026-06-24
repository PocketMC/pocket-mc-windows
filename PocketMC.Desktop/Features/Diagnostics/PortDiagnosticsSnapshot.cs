using System;
using System.Collections.Generic;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Features.Diagnostics;

/// <summary>
/// Represents the port-related diagnostics exported in a support bundle.
/// </summary>
public sealed class PortDiagnosticsSnapshot
{
    public string SchemaVersion { get; set; } = "1.1";
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string AppRootState { get; set; } = "Unknown";
    public List<PortDiagnosticsInstanceMapping> InstancePortMappings { get; set; } = new();
    public List<PortDiagnosticsLease> LeaseRegistryState { get; set; } = new();
    public List<PortDiagnosticsFailure> RecentPortFailures { get; set; } = new();
    public List<PortDiagnosticsRecoveryHistory> RecoveryHistory { get; set; } = new();
    public PortDiagnosticsTunnelState TunnelState { get; set; } = new();
    public List<PortDiagnosticsDependencyHealth> PublicConnectivityDependencies { get; set; } = new();
}

public sealed class PortDiagnosticsInstanceMapping
{
    public Guid InstanceId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string? ServerType { get; set; }
    public bool HasGeyser { get; set; }
    public bool InstancePathPresent { get; set; }
    public bool CurrentPreflightSuccessful { get; set; }
    public PortFailureCode CurrentPreflightFailureCode { get; set; }
    public string? CurrentPreflightFailureMessage { get; set; }
    public List<PortDiagnosticsExpectation> Ports { get; set; } = new();
}

public sealed class PortDiagnosticsExpectation
{
    public string DisplayName { get; set; } = string.Empty;
    public PortBindingRole BindingRole { get; set; }
    public PortEngine Engine { get; set; }
    public int Port { get; set; }
    public PortProtocol Protocol { get; set; }
    public PortIpMode IpMode { get; set; }
    public string? BindAddress { get; set; }
    public bool CheckTunnelAvailability { get; set; }
    public bool CheckPublicReachability { get; set; }
}

public sealed class PortDiagnosticsLease
{
    public int Port { get; set; }
    public PortProtocol Protocol { get; set; }
    public PortIpMode IpMode { get; set; }
    public string? BindAddress { get; set; }
    public Guid? InstanceId { get; set; }
    public string? InstanceName { get; set; }
    public bool InstancePathPresent { get; set; }
    public DateTimeOffset AcquiredAtUtc { get; set; }
}

public sealed class PortDiagnosticsFailure
{
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? InstanceId { get; set; }
    public string? InstanceName { get; set; }
    public string? DisplayName { get; set; }
    public PortBindingRole BindingRole { get; set; }
    public PortEngine Engine { get; set; }
    public int Port { get; set; }
    public PortProtocol Protocol { get; set; }
    public PortIpMode IpMode { get; set; }
    public string? BindAddress { get; set; }
    public PortFailureCode FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed class PortDiagnosticsRecoveryHistory
{
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? InstanceId { get; set; }
    public string? InstanceName { get; set; }
    public string? DisplayName { get; set; }
    public PortBindingRole BindingRole { get; set; }
    public PortEngine Engine { get; set; }
    public int Port { get; set; }
    public PortProtocol Protocol { get; set; }
    public PortIpMode IpMode { get; set; }
    public string? BindAddress { get; set; }
    public PortFailureCode FailureCode { get; set; }
    public PortRecoveryAction Action { get; set; }
    public bool IsTransient { get; set; }
    public double? RetryDelaySeconds { get; set; }
    public int AttemptNumber { get; set; }
    public int MaxAttempts { get; set; }
    public int? SuggestedPort { get; set; }
}

public sealed class PortDiagnosticsTunnelState
{
    public PlayitAgentState PlayitAgentState { get; set; }
    public bool PlayitAgentRunning { get; set; }
    public bool PlayitBinaryAvailable { get; set; }
    public bool PlayitAgentSecretPresent { get; set; }
    public List<PortDiagnosticsInstanceTunnelState> Instances { get; set; } = new();
}

public sealed class PortDiagnosticsInstanceTunnelState
{
    public Guid InstanceId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public bool CachedTunnelAddressPresent { get; set; }
    public string? CachedTunnelAddress { get; set; }
    public bool CachedVoiceChatTunnelAddressPresent { get; set; }
    public string? CachedVoiceChatTunnelAddress { get; set; }
    public List<int> ExpectedLocalPorts { get; set; } = new();
    public List<PortDiagnosticsExpectation> ExpectedTunnelPorts { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class PortDiagnosticsDependencyHealth
{
    public string Name { get; set; } = string.Empty;
    public DependencyHealthStatus Status { get; set; }
    public double LatencyMilliseconds { get; set; }
    public DateTime LastCheckedUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
