using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

public sealed class InstanceTunnelOrchestratorTunnelFlowTests
{
    [Fact]
    public async Task EnsureTunnelFlowAsync_WhenResolutionFails_KeepsPreviousTunnelAddress()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        InstanceMetadata metadata = workspace.CreateInstance("Java Existing", serverType: "Paper");
        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        workspace.AppState.SetTunnelAddress(metadata.Id, "old.playit.gg:25565");
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureTunnelFlowAsync(vm);

        Assert.Equal("old.playit.gg:25565", vm.TunnelAddress);
        Assert.True(vm.HasTunnelError);
    }

    [Fact]
    public async Task EnsureTunnelFlowAsync_WhenExistingTunnelHasNoPublicAddress_ShowsPendingError()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        InstanceMetadata metadata = workspace.CreateInstance("Java Pending", serverType: "Paper");
        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            port: 25565,
            connectAddresses: "[]",
            publicAllocations: "[]")));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureTunnelFlowAsync(vm);

        Assert.False(vm.HasTunnelAddress);
        Assert.True(vm.HasTunnelError);
        Assert.Contains("public address", vm.TunnelErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureTunnelFlowAsync_WhenCreatedTunnelHasNoPublicAddress_ShowsPendingError()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        InstanceMetadata metadata = workspace.CreateInstance("Java Created Pending", serverType: "Paper");
        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        bool created = false;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                created = true;
                return JsonResponse("""{"status":"success","data":{"id":"created"}}""");
            }

            return JsonResponse(created
                ? TunnelListJson(25565, "[]", "[]")
                : """{"status":"success","data":{"tunnels":[]}}""");
        });
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureTunnelFlowAsync(vm);

        Assert.True(created);
        Assert.False(vm.HasTunnelAddress);
        Assert.True(vm.HasTunnelError);
        Assert.Contains("pending", vm.TunnelErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureTunnelFlowAsync_WhenDisabledTunnelHasNoPublicAddress_KeepsPreviousTunnelAddress()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        InstanceMetadata metadata = workspace.CreateInstance("Java Disabled", serverType: "Paper");
        workspace.WriteServerProperties(metadata.Id, "server-port=25565");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        workspace.AppState.SetTunnelAddress(metadata.Id, "old.playit.gg:25565");
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            port: 25565,
            connectAddresses: "[]",
            publicAllocations: "[]",
            enabled: false)));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureTunnelFlowAsync(vm);

        Assert.Equal("old.playit.gg:25565", vm.TunnelAddress);
        Assert.True(vm.HasTunnelError);
    }

    private static InstanceCardViewModel CreateCard(PortReliabilityTestWorkspace workspace, InstanceMetadata metadata)
    {
        var processManager = workspace.CreateServerProcessManager();
        var lifecycleService = workspace.CreateServerLifecycleService(
            processManager,
            workspace.CreatePortPreflightService(processManager),
            workspace.CreatePortProbeService(),
            workspace.CreatePortLeaseRegistry(),
            workspace.CreatePortRecoveryService());
        return new InstanceCardViewModel(metadata, processManager, lifecycleService, workspace.AppState);
    }

    private static InstanceTunnelOrchestrator CreateOrchestrator(
        PortReliabilityTestWorkspace workspace,
        PlayitApiClient apiClient)
    {
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        PortProbeService probeService = workspace.CreatePortProbeService();
        PortLeaseRegistry leaseRegistry = workspace.CreatePortLeaseRegistry();
        return new InstanceTunnelOrchestrator(
            workspace.CreateTunnelService(apiClient, harness.Service),
            harness.Service,
            workspace.AppState,
            workspace.CreatePortPreflightService(),
            new PortFailureMessageService(),
            workspace.CreatePortRecoveryService(probeService, leaseRegistry),
            workspace.Registry,
            workspace.InstanceManager,
            new SilentDialogService(),
            new ImmediateDispatcher(),
            NullLogger<InstanceTunnelOrchestrator>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private static string TunnelListJson(
        int port,
        string connectAddresses,
        string publicAllocations,
        bool enabled = true,
        string tunnelType = "minecraft-java")
    {
        return $$"""
        {
          "status": "success",
          "data": {
            "tunnels": [
              {
                "id": "tunnel",
                "name": "server",
                "tunnel_type": "{{tunnelType}}",
                "user_enabled": {{enabled.ToString().ToLowerInvariant()}},
                "connect_addresses": {{connectAddresses}},
                "public_allocations": {{publicAllocations}},
                "origin": { "type": "agent", "data": { "config": { "fields": [{ "name": "local_port", "value": "{{port}}" }] } } }
              }
            ]
          }
        }
        """;
    }

    private sealed class ImmediateDispatcher : IAppDispatcher
    {
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class SilentDialogService : IDialogService
    {
        public Task<DialogResult> ShowDialogAsync(
            string title,
            string message,
            DialogType type = DialogType.Information,
            bool showCancel = false,
            string? primaryButtonText = null,
            string? secondaryButtonText = null,
            string? cancelButtonText = null)
        {
            return Task.FromResult(DialogResult.No);
        }

        public void ShowMessage(string title, string message, DialogType type = DialogType.Information)
        {
        }

        public Task<string?> OpenFolderDialogAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult<string?>(null);
        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult(Array.Empty<string>());
    }
}
