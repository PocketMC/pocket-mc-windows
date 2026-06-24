using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class PlayitHttpsTunnelProviderTests
{
    [Fact]
    public async Task StartAsync_CreatesHttpsTunnelWhenDedicatedTunnelIsMissing()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ConfigurePlayitConnection(workspace.AppState);
        var calls = new List<string>();

        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(request =>
        {
            calls.Add(request.RequestUri?.AbsolutePath ?? "");
            if (request.RequestUri?.AbsolutePath.Contains("tunnels/create", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse("""{"status":"success","data":{"id":"remote-created"}}""");
            }

            if (request.RequestUri?.AbsolutePath.Contains("tunnels/list", StringComparison.OrdinalIgnoreCase) == true)
            {
                return calls.Any(path => path.Contains("tunnels/create", StringComparison.OrdinalIgnoreCase))
                    ? JsonResponse(TunnelListJson(HttpsTunnel("remote-created", "pocketmc-remote-control", 25580, "remote.playit.plus")))
                    : JsonResponse(TunnelListJson());
            }

            return JsonResponse("""{"status":"success","data":{}}""");
        });
        var provider = CreateProvider(workspace, apiClient);

        RemoteTunnelStartResult result = await provider.StartAsync(StartRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://remote.playit.plus", result.PublicUrl);
        Assert.Equal("remote-created", workspace.AppState.Settings.RemoteControl.PlayitTunnelId);
        Assert.Contains("/v1/tunnels/create", calls);
    }

    [Fact]
    public async Task StartAsync_ReusesSavedTunnelId()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ConfigurePlayitConnection(workspace.AppState);
        workspace.AppState.Settings.RemoteControl.PlayitTunnelId = "saved-remote";
        var updateBodies = new List<string>();
        var enableBodies = new List<string>();

        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("tunnels/update", StringComparison.OrdinalIgnoreCase))
            {
                updateBodies.Add(ReadBody(request));
                return JsonResponse("""{"status":"success","data":{}}""");
            }

            if (path.Contains("tunnels/enable", StringComparison.OrdinalIgnoreCase))
            {
                enableBodies.Add(ReadBody(request));
                return JsonResponse("""{"status":"success","data":{}}""");
            }

            return JsonResponse(TunnelListJson(HttpsTunnel("saved-remote", "pocketmc-remote-control", 12345, "saved.playit.plus")));
        });
        var provider = CreateProvider(workspace, apiClient);

        RemoteTunnelStartResult result = await provider.StartAsync(StartRequest(localPort: 25581), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://saved.playit.plus", result.PublicUrl);
        Assert.Single(updateBodies);
        Assert.Contains("\"tunnel_id\":\"saved-remote\"", updateBodies[0]);
        Assert.Contains("\"local_ip\":\"127.0.0.1\"", updateBodies[0]);
        Assert.Contains("\"local_port\":25581", updateBodies[0]);
        Assert.Single(enableBodies);
        Assert.Contains("\"enabled\":true", enableBodies[0]);
    }

    [Fact]
    public async Task StartAsync_ReusesDedicatedPocketMcRemoteControlTunnelByName()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ConfigurePlayitConnection(workspace.AppState);

        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            JsonResponse(TunnelListJson(HttpsTunnel("named-remote", "pocketmc-remote-control", 25580, "named.playit.plus"))));
        var provider = CreateProvider(workspace, apiClient);

        RemoteTunnelStartResult result = await provider.StartAsync(StartRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://named.playit.plus", result.PublicUrl);
        Assert.Equal("named-remote", workspace.AppState.Settings.RemoteControl.PlayitTunnelId);
    }

    [Fact]
    public async Task StopAsync_WhenSavedIdPointsToMinecraftTunnel_DoesNotDisableIt()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ConfigurePlayitConnection(workspace.AppState);
        workspace.AppState.Settings.RemoteControl.PlayitTunnelId = "java-tunnel";
        var enableBodies = new List<string>();

        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("tunnels/enable", StringComparison.OrdinalIgnoreCase))
            {
                enableBodies.Add(ReadBody(request));
                return JsonResponse("""{"status":"success","data":{}}""");
            }

            return JsonResponse(TunnelListJson(MinecraftJavaTunnel("java-tunnel", 25565)));
        });
        var provider = CreateProvider(workspace, apiClient);

        await provider.StopAsync(CancellationToken.None);

        Assert.Empty(enableBodies);
        Assert.Contains("PocketMC Remote Control", provider.GetStatus().ErrorMessage);
    }

    [Fact]
    public async Task StopAsync_DisablesPlayitTunnelThroughApi()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ConfigurePlayitConnection(workspace.AppState);
        workspace.AppState.Settings.RemoteControl.PlayitTunnelId = "remote-stop";
        var enableBodies = new List<string>();

        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("tunnels/enable", StringComparison.OrdinalIgnoreCase))
            {
                enableBodies.Add(ReadBody(request));
                return JsonResponse("""{"status":"success","data":{}}""");
            }

            return JsonResponse(TunnelListJson(HttpsTunnel("remote-stop", "pocketmc-remote-control", 25580, "stop.playit.plus")));
        });
        var provider = CreateProvider(workspace, apiClient);

        await provider.StopAsync(CancellationToken.None);

        string body = Assert.Single(enableBodies);
        Assert.Contains("\"tunnel_id\":\"remote-stop\"", body);
        Assert.Contains("\"enabled\":false", body);
        Assert.False(provider.GetStatus().IsRunning);
    }

    [Fact]
    public async Task StartAsync_WhenPlayitPremiumIsRequired_ReturnsFriendlyError()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        ConfigurePlayitConnection(workspace.AppState);

        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("tunnels/create", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse("""{"status":"fail","data":"RequiresPlayitPremium"}""");
            }

            return JsonResponse(TunnelListJson());
        });
        var provider = CreateProvider(workspace, apiClient);

        RemoteTunnelStartResult result = await provider.StartAsync(StartRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("PlayIt HTTPS tunnels require PlayIt Premium.", result.ErrorMessage);
        Assert.Equal(result.ErrorMessage, provider.GetStatus().ErrorMessage);
    }

    private static PlayitHttpsTunnelProvider CreateProvider(
        PortReliabilityTestWorkspace workspace,
        PlayitApiClient apiClient)
    {
        string settingsPath = Path.Combine(workspace.RootPath, "settings.json");
        return new PlayitHttpsTunnelProvider(
            apiClient,
            workspace.AppState,
            new SettingsManager(settingsPath),
            NullLogger<PlayitHttpsTunnelProvider>.Instance);
    }

    private static RemoteTunnelStartRequest StartRequest(int localPort = 25580) => new()
    {
        LocalPort = localPort,
        LocalUrl = $"http://127.0.0.1:{localPort}"
    };

    private static void ConfigurePlayitConnection(ApplicationState state)
    {
        state.Settings.PlayitPartnerConnection = new PlayitPartnerConnection
        {
            AgentId = "agent-1",
            AgentSecretKey = "test-secret"
        };
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private static string ReadBody(HttpRequestMessage request) =>
        request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

    private static string TunnelListJson(params string[] tunnelBodies)
    {
        string tunnels = string.Join(",", tunnelBodies);
        return $$"""
        {
          "status": "success",
          "data": {
            "tunnels": [{{tunnels}}]
          }
        }
        """;
    }

    private static string HttpsTunnel(string id, string name, int localPort, string publicAddress)
    {
        return $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "tunnel_type": "https",
          "user_enabled": true,
          "connect_addresses": [{ "type": "domain", "value": { "hostname": "{{publicAddress}}" } }],
          "public_allocations": [],
          "origin": {
            "type": "agent",
            "data": {
              "agent_id": "agent-1",
              "config": {
                "fields": [
                  { "name": "local_ip", "value": "127.0.0.1" },
                  { "name": "local_port", "value": "{{localPort}}" }
                ]
              }
            }
          }
        }
        """;
    }

    private static string MinecraftJavaTunnel(string id, int localPort)
    {
        return $$"""
        {
          "id": "{{id}}",
          "name": "Java server",
          "tunnel_type": "minecraft-java",
          "user_enabled": true,
          "connect_addresses": [{ "type": "domain", "value": { "address": "java.playit.gg:25565" } }],
          "public_allocations": [],
          "origin": {
            "type": "agent",
            "data": {
              "agent_id": "agent-1",
              "config": {
                "fields": [{ "name": "local_port", "value": "{{localPort}}" }]
              }
            }
          }
        }
        """;
    }
}


