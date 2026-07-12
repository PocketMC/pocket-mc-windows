using System.Net;
using PocketMC.Desktop.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests;

public sealed class SimpleVoiceChatFirstRunTunnelTests
{
    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenConfigMissing_CreatesTunnelAndConfigBeforeStart()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice First Run", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        string? createPayload = null;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                using var reader = new StreamReader(req.Content!.ReadAsStream());
                createPayload = reader.ReadToEnd();
                return JsonResponse("""{"status":"success","data":{"id":"voice"}}""");
            }

            return JsonResponse(createPayload == null
                ? """{"status":"success","data":{"tunnels":[]}}"""
                : VoiceTunnelListJson("voice.playit.gg:30000", 24454));
        });
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, DialogResult.Yes);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        bool shouldStart = await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);
        bool configExistsBeforeStart = File.Exists(Path.Combine(
            workspace.GetInstancePath(metadata.Id),
            "config",
            "voicechat",
            "voicechat-server.properties"));

        Assert.True(shouldStart);
        Assert.True(configExistsBeforeStart);
        Assert.NotNull(createPayload);
        Assert.Contains("mc-simple-voice-chat", createPayload);
        Assert.DoesNotContain("minecraft-bedrock", createPayload);
        Assert.DoesNotContain("raw-ports", createPayload);

        string config = File.ReadAllText(Path.Combine(
            workspace.GetInstancePath(metadata.Id),
            "config",
            "voicechat",
            "voicechat-server.properties"));
        Assert.Contains("port=24454", config);
        Assert.Contains("bind_address=*", config);
        Assert.Contains("voice_host=voice.playit.gg:30000", config);
    }

    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenPluginJarAndConfigMissing_CreatesPluginConfigBeforeStart()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Plugin First Run", serverType: "Paper");
        workspace.WriteFile(metadata.Id, Path.Combine("plugins", "voicechat-bukkit.jar"), "jar");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        string? createPayload = null;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                using var reader = new StreamReader(req.Content!.ReadAsStream());
                createPayload = reader.ReadToEnd();
                return JsonResponse("""{"status":"success","data":{"id":"voice"}}""");
            }

            return JsonResponse(createPayload == null
                ? """{"status":"success","data":{"tunnels":[]}}"""
                : VoiceTunnelListJson("voice.playit.gg:30000", 24454));
        });
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, DialogResult.Yes);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        bool shouldStart = await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);
        string pluginConfigPath = Path.Combine(
            workspace.GetInstancePath(metadata.Id),
            "plugins",
            "voicechat",
            "voicechat-server.properties");
        string modConfigPath = Path.Combine(
            workspace.GetInstancePath(metadata.Id),
            "config",
            "voicechat",
            "voicechat-server.properties");

        Assert.True(shouldStart);
        Assert.True(File.Exists(pluginConfigPath));
        Assert.False(File.Exists(modConfigPath));
        string config = File.ReadAllText(pluginConfigPath);
        Assert.Contains("port=24454", config);
        Assert.Contains("bind_address=*", config);
        Assert.Contains("voice_host=voice.playit.gg:30000", config);
    }

    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenConfigExists_PatchesWithoutOverwritingComments()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Existing", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("config", "voicechat", "voicechat-server.properties"),
            """
            # keep this
            port=25000
            bind_address=*
            voice_host=old.example.com:24454
            custom=value
            """);
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            JsonResponse(VoiceTunnelListJson("voice.playit.gg:30000", 25000)));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, DialogResult.Yes);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);

        string config = File.ReadAllText(Path.Combine(
            workspace.GetInstancePath(metadata.Id),
            "config",
            "voicechat",
            "voicechat-server.properties"));
        Assert.Contains("# keep this", config);
        Assert.Contains("custom=value", config);
        Assert.Contains("port=25000", config);
        Assert.Contains("voice_host=voice.playit.gg:30000", config);
        Assert.DoesNotContain("old.example.com", config);
    }

    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenUserDeclines_StartsWithWarningAndNoTunnel()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Decline", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        bool createCalled = false;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                createCalled = true;
            }

            return JsonResponse("""{"status":"success","data":{"tunnels":[]}}""");
        });
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, DialogResult.No);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        bool shouldStart = await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);

        Assert.True(shouldStart);
        Assert.False(createCalled);
        Assert.True(vm.HasSimpleVoiceChatWarning);
        Assert.Equal("StartedWithoutVoiceTunnel", vm.Metadata.SimpleVoiceChatStatus);
    }

    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenDontAskAgain_SuppressesNextPromptButKeepsWarning()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Dismiss", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        var dialog = new RecordingDialogService(DialogResult.Cancel);
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            JsonResponse("""{"status":"success","data":{"tunnels":[]}}"""));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, dialog);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);
        await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);

        Assert.True(vm.Metadata.SimpleVoiceChatPromptDismissed);
        Assert.Equal(1, dialog.ShowCount);
        Assert.True(vm.HasSimpleVoiceChatWarning);
    }

    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenDialogIsClosed_DoesNotDismissFuturePrompt()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Close", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        var dialog = new RecordingDialogService(DialogResult.Dismiss);
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            JsonResponse("""{"status":"success","data":{"tunnels":[]}}"""));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, dialog);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);

        await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);
        await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);

        Assert.False(vm.Metadata.SimpleVoiceChatPromptDismissed);
        Assert.Equal(2, dialog.ShowCount);
        Assert.True(vm.HasSimpleVoiceChatWarning);
    }

    [Fact]
    public async Task EnsureSimpleVoiceChatBeforeStartAsync_WhenServerAlreadyRunningAndVoiceHostChanges_ShowsRestartRequired()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var metadata = workspace.CreateInstance("Voice Restart", serverType: "Fabric");
        workspace.WriteFile(metadata.Id, Path.Combine("mods", "voicechat-2.5.0.jar"), "jar");
        workspace.WriteFile(
            metadata.Id,
            Path.Combine("config", "voicechat", "voicechat-server.properties"),
            """
            port=24454
            bind_address=*
            voice_host=old.example.com:24454
            """);
        workspace.WritePlayitSecret();
        workspace.EnsurePlayitBinaryExists();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            JsonResponse(VoiceTunnelListJson("voice.playit.gg:30000", 24454)));
        InstanceTunnelOrchestrator orchestrator = CreateOrchestrator(workspace, apiClient, DialogResult.Yes);
        InstanceCardViewModel vm = CreateCard(workspace, metadata);
        vm.UpdateState(ServerState.Online);

        await orchestrator.EnsureSimpleVoiceChatBeforeStartAsync(vm);

        Assert.Equal("Restart required for Simple Voice Chat tunnel changes to apply.", vm.SimpleVoiceChatWarning);
    }

    private static InstanceCardViewModel CreateCard(PortReliabilityTestWorkspace workspace, PocketMC.Domain.Models.InstanceMetadata metadata)
    {
        var processManager = workspace.CreateServerProcessManager();
        var lifecycleService = workspace.CreateServerLifecycleService(
            processManager,
            workspace.CreatePortPreflightService(processManager),
            workspace.CreatePortProbeService(),
            workspace.CreatePortLeaseRegistry(),
            workspace.CreatePortRecoveryService());
        return new InstanceCardViewModel(
            metadata,
            processManager,
            lifecycleService,
            workspace.AppState,
            workspace.Registry,
            new PocketMC.Infrastructure.Instances.GeyserDetector(),
            new PocketMC.Application.Services.Networking.SimpleVoiceChatDetector());
    }

    private static InstanceTunnelOrchestrator CreateOrchestrator(
        PortReliabilityTestWorkspace workspace,
        PlayitApiClient apiClient,
        DialogResult dialogResult)
    {
        return CreateOrchestrator(workspace, apiClient, new RecordingDialogService(dialogResult));
    }

    private static InstanceTunnelOrchestrator CreateOrchestrator(
        PortReliabilityTestWorkspace workspace,
        PlayitApiClient apiClient,
        IDialogService dialogService)
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
            dialogService,
            new ImmediateDispatcher(),
            NullLogger<InstanceTunnelOrchestrator>.Instance,
            new PocketMC.Application.Services.Networking.SimpleVoiceChatDetector());
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private static string VoiceTunnelListJson(string publicAddress, int port)
    {
        return $$"""
        {
          "status": "success",
          "data": {
            "tunnels": [
              {
                "id": "voice",
                "name": "voice",
                "tunnel_type": "mc-simple-voice-chat",
                "user_enabled": true,
                "connect_addresses": [{ "type": "domain", "value": { "address": "{{publicAddress}}" } }],
                "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.5", "port": 30000 } }],
                "origin": { "type": "agent", "details": { "agent_id": "test-agent", "config_data": { "fields": [{ "name": "local_port", "value": "{{port}}" }] } } }
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

    private sealed class RecordingDialogService : IDialogService
    {
        private readonly DialogResult _result;

        public RecordingDialogService(DialogResult result)
        {
            _result = result;
        }

        public int ShowCount { get; private set; }

        public Task<DialogResult> ShowDialogAsync(
            string title,
            string message,
            DialogType type = DialogType.Information,
            bool showCancel = false,
            string? primaryButtonText = null,
            string? secondaryButtonText = null,
            string? cancelButtonText = null,
            string? linkText = null,
            string? linkUrl = null)
        {
            ShowCount++;
            return Task.FromResult(_result);
        }

        public void ShowMessage(string title, string message, DialogType type = DialogType.Information)
        {
        }

        public Task<string?> OpenFolderDialogAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult<string?>(null);
        public Task<string?> PromptPasswordAsync(string title, string message) => Task.FromResult<string?>(null);
        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*") => Task.FromResult(Array.Empty<string>());
        public Task ShowProgressDialogAsync(string title, string message, Func<IProgress<double>, Task> action) => action(new Progress<double>());
    }
}


