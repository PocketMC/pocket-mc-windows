using System;
using System.IO;
using System.Windows;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceCardViewModelTests
{
    [Fact]
    public void Constructor_WithGeyserBedrockPort_SetsBedrockLocalPort()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Geyser Server", serverType: "Paper");
        metadata.HasGeyser = true;
        metadata.GeyserBedrockPort = 19145;
        workspace.SaveMetadata(metadata);
        workspace.WriteFile(metadata.Id, Path.Combine("plugins", "Geyser.jar"), "jar");

        var processManager = workspace.CreateServerProcessManager();
        var probeService = workspace.CreatePortProbeService();
        var leaseRegistry = workspace.CreatePortLeaseRegistry();
        var recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);
        var lifecycleService = workspace.CreateServerLifecycleService(processManager, workspace.CreatePortPreflightService(processManager), probeService, leaseRegistry, recoveryService);

        var vm = new InstanceCardViewModel(metadata, processManager, lifecycleService, workspace.AppState, workspace.Registry, new PocketMC.Desktop.Helpers.GeyserDetector(), new PocketMC.Desktop.Features.Networking.SimpleVoiceChatDetector());

        Assert.Equal(19145, vm.BedrockLocalPort);
        Assert.Contains("19145", vm.BedrockIpDisplayText);
    }

    [Fact]
    public void Constructor_WithGeyserEnabled_ShowsCrossPlayBadge()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Crossplay Server", serverType: "Paper");
        metadata.HasGeyser = true;
        workspace.WriteFile(metadata.Id, Path.Combine("plugins", "Geyser.jar"), "jar");
        var vm = CreateViewModel(workspace, metadata);

        Assert.True(vm.ShowCrossPlayBadge);
        Assert.Equal("Cross-play", vm.CrossPlayBadgeText);
        Assert.Contains("Java", vm.CrossPlayBadgeTooltip);
        Assert.Contains("Bedrock", vm.CrossPlayBadgeTooltip);
    }

    [Fact]
    public void Constructor_WithNativeBedrock_DoesNotShowCrossPlayBadge()
    {
        var metadata = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Bedrock Server",
            ServerType = "Bedrock Dedicated Server",
            HasGeyser = false
        };

        using var workspace = new PortReliabilityTestWorkspace();
        var vm = CreateViewModel(workspace, metadata);

        Assert.False(vm.ShowCrossPlayBadge);
    }

    [Fact]
    public void Constructor_WithVoiceChatEnabled_ShowsVoiceChatBadge()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Server", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        var vm = CreateViewModel(workspace, metadata);

        Assert.True(vm.ShowVoiceChatBadge);
        Assert.Equal("Voice Chat", vm.VoiceChatBadgeText);
        Assert.Equal(Visibility.Visible, vm.VoiceChatBadgeVisibility);
        Assert.Contains("Voice Chat", vm.VoiceChatBadgeTooltip);
    }

    [Fact]
    public void Constructor_WithLastPlayedAt_FormatsLastPlayedText()
    {
        var lastPlayed = DateTime.UtcNow.AddHours(-3);
        var metadata = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Recent Server",
            ServerType = "Paper",
            LastPlayedAt = lastPlayed
        };

        using var workspace = new PortReliabilityTestWorkspace();
        var vm = CreateViewModel(workspace, metadata);

        Assert.StartsWith("Last played: ", vm.LastPlayedText);
        Assert.Contains(lastPlayed.ToLocalTime().ToString("MMM d, yyyy"), vm.LastPlayedText);
        // LastPlayedValueText now shows relative time instead of exact date
        Assert.Contains("ago", vm.LastPlayedValueText);
        // Tooltip retains the exact date
        Assert.Equal(lastPlayed.ToLocalTime().ToString("MMM d, yyyy h:mm tt"), vm.LastPlayedTooltip);
    }

    [Fact]
    public void Constructor_WithoutLastPlayedAt_ShowsNeverPlayedText()
    {
        var metadata = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Fresh Server",
            ServerType = "Paper"
        };

        using var workspace = new PortReliabilityTestWorkspace();
        var vm = CreateViewModel(workspace, metadata);

        Assert.Equal("Last played: Never", vm.LastPlayedText);
        Assert.Equal("Never", vm.LastPlayedValueText);
        Assert.Equal("Never played", vm.LastPlayedTooltip);
    }

    [Fact]
    public void FormatRelativeTime_JustNow()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddSeconds(-30));
        Assert.Equal("Just now", result);
    }

    [Fact]
    public void FormatRelativeTime_Minutes()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal("5 min ago", result);
    }

    [Fact]
    public void FormatRelativeTime_SingleMinute()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddMinutes(-1).AddSeconds(-10));
        Assert.Equal("1 min ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Hours()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddHours(-3));
        Assert.Equal("3 hours ago", result);
    }

    [Fact]
    public void FormatRelativeTime_SingleHour()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddHours(-1).AddMinutes(-10));
        Assert.Equal("1 hour ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Yesterday()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddDays(-1).AddHours(-1));
        Assert.Equal("Yesterday", result);
    }

    [Fact]
    public void FormatRelativeTime_Days()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddDays(-4));
        Assert.Equal("4 days ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Weeks()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddDays(-14));
        Assert.Equal("2 weeks ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Months()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddDays(-90));
        Assert.Equal("3 months ago", result);
    }

    [Fact]
    public void FormatRelativeTime_Years()
    {
        var result = InstanceCardViewModel.FormatRelativeTime(DateTime.UtcNow.AddDays(-400));
        Assert.Equal("1 year ago", result);
    }

    private static InstanceCardViewModel CreateViewModel(PortReliabilityTestWorkspace workspace, InstanceMetadata metadata)
    {
        var processManager = workspace.CreateServerProcessManager();
        var probeService = workspace.CreatePortProbeService();
        var leaseRegistry = workspace.CreatePortLeaseRegistry();
        var recoveryService = workspace.CreatePortRecoveryService(probeService, leaseRegistry);
        var lifecycleService = workspace.CreateServerLifecycleService(
            processManager,
            workspace.CreatePortPreflightService(processManager),
            probeService,
            leaseRegistry,
            recoveryService);

        return new InstanceCardViewModel(metadata, processManager, lifecycleService, workspace.AppState, workspace.Registry, new PocketMC.Desktop.Helpers.GeyserDetector(), new PocketMC.Desktop.Features.Networking.SimpleVoiceChatDetector());
    }
}
