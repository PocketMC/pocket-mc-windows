using PocketMC.Desktop.Features.Diagnostics;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests;

public sealed class PortDiagnosticsSnapshotBuilderTests
{
    [Fact]
    public void Build_IncludesMappingsLeasesFailuresAndRedactedTunnelState()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ServerProcessManager processManager = workspace.CreateServerProcessManager();
        PortPreflightService preflightService = workspace.CreatePortPreflightService(processManager);
        PortLeaseRegistry leaseRegistry = workspace.CreatePortLeaseRegistry();
        PortProbeService probeService = workspace.CreatePortProbeService();
        PortRecoveryService recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);

        var metadata = workspace.CreateInstance("Diagnostics", serverType: "Paper");
        metadata.HasGeyser = true;
        metadata.GeyserBedrockPort = 19140;
        workspace.SaveMetadata(metadata);
        string instancePath = workspace.GetInstancePath(metadata.Id);
        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("plugins", "Geyser-Spigot", "config.yml"),
            """
            bedrock:
              address: 0.0.0.0
              port: 19140
              clone-remote-port: false
            """);

        workspace.AppState.SetTunnelAddress(metadata.Id, "public.example.com:25565");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();

        var javaLease = new PortLease(
            25565,
            PortProtocol.Tcp,
            PortIpMode.DualStack,
            metadata.Id,
            metadata.Name,
            instancePath,
            bindAddress: null);
        Assert.True(leaseRegistry.TryReserve(javaLease, out _));

        var failureRequest = new PortCheckRequest(
            19140,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            instanceId: metadata.Id,
            instanceName: metadata.Name,
            instancePath: instancePath,
            bindingRole: PortBindingRole.GeyserBedrock,
            engine: PortEngine.Geyser,
            displayName: "Geyser Bedrock");
        var failureResult = new PortCheckResult(
            failureRequest,
            isSuccessful: false,
            canBindLocally: true,
            failureCode: PortFailureCode.PublicReachabilityFailure,
            failureMessage: "No public address resolved.");
        recoveryService.Recommend(failureResult);

        PlayitApiClient playitApiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        DependencyHealthMonitor dependencyHealthMonitor = workspace.CreateDependencyHealthMonitor();
        PortDiagnosticsSnapshotBuilder builder = workspace.CreateDiagnosticsSnapshotBuilder(
            preflightService,
            leaseRegistry,
            recoveryService,
            harness.Service,
            playitApiClient,
            dependencyHealthMonitor);

        PortDiagnosticsSnapshot snapshot = builder.Build();

        Assert.Equal("1.1", snapshot.SchemaVersion);
        Assert.Single(snapshot.InstancePortMappings);
        Assert.Single(snapshot.LeaseRegistryState);
        Assert.Single(snapshot.RecentPortFailures);
        Assert.Single(snapshot.RecoveryHistory);

        PortDiagnosticsInstanceMapping mapping = snapshot.InstancePortMappings[0];
        Assert.True(mapping.CurrentPreflightSuccessful);
        Assert.Equal(2, mapping.Ports.Count);
        Assert.Contains(mapping.Ports, p => p.BindingRole == PortBindingRole.JavaServer && p.Protocol == PortProtocol.Tcp);
        Assert.Contains(mapping.Ports, p => p.BindingRole == PortBindingRole.GeyserBedrock && p.Port == 19140 && p.Protocol == PortProtocol.Udp);

        PortDiagnosticsFailure failure = snapshot.RecentPortFailures[0];
        Assert.Equal(PortFailureCode.PublicReachabilityFailure, failure.FailureCode);
        Assert.Equal(PortBindingRole.GeyserBedrock, failure.BindingRole);
        Assert.Equal(PortEngine.Geyser, failure.Engine);

        Assert.Equal(PlayitAgentState.Connected, snapshot.TunnelState.PlayitAgentState);
        Assert.True(snapshot.TunnelState.PlayitBinaryAvailable);
        Assert.True(snapshot.TunnelState.PlayitAgentSecretPresent);
        Assert.Single(snapshot.TunnelState.Instances);
        Assert.True(snapshot.TunnelState.Instances[0].CachedTunnelAddressPresent);
        Assert.Equal("[REDACTED_PUBLIC_ADDRESS]", snapshot.TunnelState.Instances[0].CachedTunnelAddress);
        Assert.Equal(2, snapshot.TunnelState.Instances[0].ExpectedTunnelPorts.Count);

        Assert.Contains(snapshot.PublicConnectivityDependencies, dependency => dependency.Name == "Playit.gg API");
    }
}
