using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Tests;

public sealed class ServerLifecycleServiceTests
{
    [Fact]
    public async Task StartAsync_WhenLaunchFailsAfterLeaseAcquisition_ReleasesReservedPorts()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ServerProcessManager processManager = workspace.CreateServerProcessManager();
        PortPreflightService preflightService = workspace.CreatePortPreflightService(processManager);
        PortProbeService probeService = workspace.CreatePortProbeService();
        PortLeaseRegistry leaseRegistry = workspace.CreatePortLeaseRegistry();
        PortRecoveryService recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);
        var notifications = new RecordingNotificationService();
        ServerLifecycleService lifecycleService = workspace.CreateServerLifecycleService(
            processManager,
            preflightService,
            probeService,
            leaseRegistry,
            recoveryService,
            notifications);

        int port = workspace.GetAvailableUdpPort();
        var metadata = workspace.CreateInstance("Bedrock Failure", serverType: "Bedrock");
        workspace.WriteServerProperties(metadata.Id, $"server-port={port}");

        await Assert.ThrowsAsync<FileNotFoundException>(() => lifecycleService.StartAsync(metadata));

        Assert.False(lifecycleService.IsRunning(metadata.Id));
        Assert.Empty(leaseRegistry.GetLeasesForInstance(metadata.Id));
        Assert.Null(workspace.AppState.GetTunnelAddress(metadata.Id));
        Assert.Empty(notifications.Messages);
    }

    [Fact]
    public void KillAll_WhenResidualNetworkingStateExists_ClearsLeasesAndCachedTunnelAddresses()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ServerProcessManager processManager = workspace.CreateServerProcessManager();
        PortPreflightService preflightService = workspace.CreatePortPreflightService(processManager);
        PortProbeService probeService = workspace.CreatePortProbeService();
        PortLeaseRegistry leaseRegistry = workspace.CreatePortLeaseRegistry();
        PortRecoveryService recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);
        ServerLifecycleService lifecycleService = workspace.CreateServerLifecycleService(
            processManager,
            preflightService,
            probeService,
            leaseRegistry,
            recoveryService);

        var metadata = workspace.CreateInstance("Residual Networking", serverType: "Paper");
        string instancePath = workspace.GetInstancePath(metadata.Id);
        var lease = new PortLease(
            25565,
            PortProtocol.Tcp,
            PortIpMode.DualStack,
            metadata.Id,
            metadata.Name,
            instancePath,
            bindAddress: null);

        Assert.True(leaseRegistry.TryReserve(lease, out _));
        workspace.AppState.SetTunnelAddress(metadata.Id, "public.example.com:25565");

        lifecycleService.KillAll();

        Assert.Empty(leaseRegistry.GetLeasesForInstance(metadata.Id));
        Assert.Null(workspace.AppState.GetTunnelAddress(metadata.Id));
    }

    [Fact]
    public void CrashEventHandler_LogsCancellationSeparatelyFromUnexpectedFailures()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Instances",
            "Services",
            "ServerLifecycleService.cs"));

        Assert.Contains("catch (OperationCanceledException)", source, StringComparison.Ordinal);
        Assert.Contains("Crash recovery was cancelled for instance {InstanceId}.", source, StringComparison.Ordinal);
        Assert.Contains("Unhandled error while processing crash recovery for instance {InstanceId}.", source, StringComparison.Ordinal);
    }
}
