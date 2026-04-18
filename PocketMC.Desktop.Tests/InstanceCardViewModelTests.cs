using System;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceCardViewModelTests
{
    [Fact]
    public void Constructor_WithGeyserBedrockPort_SetsBedrockLocalPort()
    {
        var metadata = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Geyser Server",
            ServerType = "Paper",
            HasGeyser = true,
            GeyserBedrockPort = 19145
        };

        using var workspace = new PortReliabilityTestWorkspace();
        var processManager = workspace.CreateServerProcessManager();
        var probeService = workspace.CreatePortProbeService();
        var leaseRegistry = workspace.CreatePortLeaseRegistry();
        var recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);
        var lifecycleService = workspace.CreateServerLifecycleService(processManager, workspace.CreatePortPreflightService(processManager), probeService, leaseRegistry, recoveryService);

        var vm = new InstanceCardViewModel(metadata, processManager, lifecycleService);

        Assert.Equal(19145, vm.BedrockLocalPort);
        Assert.Contains("19145", vm.BedrockIpDisplayText);
    }
}
