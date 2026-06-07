using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests;

public sealed class PlayitApiClientTunnelParsingTests
{
    [Theory]
    [InlineData("""{ "details": { "config_data": { "fields": [{ "name": "local_port", "value": "25565" }] } } }""")]
    [InlineData("""{ "data": { "config": { "fields": [{ "name": "local_port", "value": "25565" }] } } }""")]
    [InlineData("""{ "details": { "config": { "fields": [{ "name": "local_port", "value": "25565" }] } } }""")]
    [InlineData("""{ "data": { "config_data": { "fields": [{ "name": "local_port", "value": "25565" }] } } }""")]
    public async Task GetTunnelsAsync_ExtractsLocalPortFromSupportedOriginShapes(string originBody)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            originBody,
            """[{ "type": "domain", "value": { "address": "java.playit.gg:12345" } }]""")));

        TunnelListResult result = await apiClient.GetTunnelsAsync();

        Assert.True(result.Success);
        TunnelData tunnel = Assert.Single(result.Tunnels);
        Assert.Equal(25565, tunnel.Port);
        Assert.Equal("java.playit.gg:12345", tunnel.PublicAddress);
    }

    [Fact]
    public async Task GetTunnelsAsync_ExactLocalPortWinsOverGenericPortField()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            """
            { "data": { "config": { "fields": [
              { "name": "port", "value": "12345" },
              { "name": "local_port", "value": "25565" }
            ] } } }
            """,
            """[{ "type": "domain", "value": { "address": "java.playit.gg:12345" } }]""")));

        TunnelData tunnel = Assert.Single((await apiClient.GetTunnelsAsync()).Tunnels);

        Assert.Equal(25565, tunnel.Port);
    }

    [Theory]
    [InlineData("""[{ "type": "domain", "value": { "address": "one.playit.gg:10001" } }]""", "one.playit.gg:10001")]
    [InlineData("""[{ "type": "domain", "value": { "hostname": "two.playit.gg", "port": 10002 } }]""", "two.playit.gg:10002")]
    [InlineData("""[{ "type": "auto", "value": { "host": "three.playit.gg", "port": "10003" } }]""", "three.playit.gg:10003")]
    [InlineData("""[{ "type": "addr4", "value": { "address": "127.0.0.1:10004" } }]""", "127.0.0.1:10004")]
    [InlineData("""[{ "type": "domain", "value": "five.playit.gg:10005" }]""", "five.playit.gg:10005")]
    [InlineData("""[{ "type": "ip4", "value": { "ip": "10.0.0.6", "default_port": 10006 } }]""", "10.0.0.6:10006")]
    [InlineData("""[{ "type": "domain", "value": { "address": "seven.playit.gg:10007", "port": 20000 } }]""", "seven.playit.gg:10007")]
    public async Task GetTunnelsAsync_ExtractsPublicAddressFromSupportedValueShapes(string connectAddresses, string expected)
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            """{ "details": { "config_data": { "fields": [{ "name": "local_port", "value": "25565" }] } } }""",
            connectAddresses)));

        TunnelData tunnel = Assert.Single((await apiClient.GetTunnelsAsync()).Tunnels);

        Assert.Equal(expected, tunnel.PublicAddress);
    }

    [Fact]
    public async Task FindTunnelForRequest_MatchesJavaTunnelParsedFromOriginDataConfig()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            """{ "data": { "config": { "fields": [{ "name": "local_port", "value": "25565" }] } } }""",
            """[{ "type": "domain", "value": { "hostname": "java.playit.gg", "port": 25565 } }]""")));
        TunnelListResult result = await apiClient.GetTunnelsAsync();

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(
            result.Tunnels,
            new PortCheckRequest(25565, PortProtocol.Tcp, PortIpMode.IPv4));

        Assert.NotNull(match);
        Assert.Equal("java.playit.gg:25565", match.PublicAddress);
    }

    [Fact]
    public async Task FindTunnelForRequest_MatchesBedrockTunnelParsedFromOriginDataConfig()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            """{ "data": { "config": { "fields": [{ "name": "local-port", "value": "19132" }] } } }""",
            """[{ "type": "domain", "value": { "host": "bedrock.playit.gg", "port": 19132 } }]""",
            tunnelType: "minecraft-bedrock")));
        TunnelListResult result = await apiClient.GetTunnelsAsync();

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(
            result.Tunnels,
            new PortCheckRequest(19132, PortProtocol.Udp, PortIpMode.IPv4));

        Assert.NotNull(match);
        Assert.Equal("bedrock.playit.gg:19132", match.PublicAddress);
    }

    [Fact]
    public async Task GetTunnelsAsync_WhenOriginPortMissing_KeepsPortZeroForDiagnostics()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            """{ "data": { "config": { "fields": [] } } }""",
            """[{ "type": "domain", "value": { "address": "unknown.playit.gg:10000" } }]""")));

        TunnelData tunnel = Assert.Single((await apiClient.GetTunnelsAsync()).Tunnels);

        Assert.Equal(0, tunnel.Port);
        Assert.Null(PlayitApiClient.FindTunnelForRequest(
            new[] { tunnel },
            new PortCheckRequest(25565, PortProtocol.Tcp, PortIpMode.IPv4)));
    }

    [Fact]
    public async Task FindHttpTunnelForPort_MatchesPlayitHttpsTunnelForRemoteDashboard()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ => JsonResponse(TunnelListJson(
            """{ "data": { "config": { "fields": [{ "name": "local_port", "value": "25580" }] } } }""",
            """[{ "type": "domain", "value": { "hostname": "remote.playit.plus" } }]""",
            tunnelType: "https")));

        TunnelListResult result = await apiClient.GetTunnelsAsync();

        TunnelData? match = PlayitApiClient.FindHttpTunnelForPort(result.Tunnels, 25580);
        Assert.NotNull(match);
        Assert.Equal("remote.playit.plus", match.PublicAddress);
    }

    [Fact]
    public async Task CreateHttpTunnelAsync_SendsHttpsTunnelTypeAndRemotePort()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        string? requestBody = null;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(request =>
        {
            using Stream stream = request.Content!.ReadAsStream();
            using var reader = new StreamReader(stream);
            requestBody = reader.ReadToEnd();
            return JsonResponse("""{"status":"success","data":{"id":"remote-http"}}""");
        });

        TunnelCreateResult result = await apiClient.CreateHttpTunnelAsync("pocketmc-remote", 25580);

        Assert.True(result.Success);
        Assert.Equal("remote-http", result.TunnelId);
        Assert.Contains("\"details\":\"https\"", requestBody);
        Assert.Contains("\"local_port\"", requestBody);
        Assert.Contains("\"25580\"", requestBody);
    }

    [Fact]
    public async Task CreateHttpTunnelAsync_ReportsWhenPlayitPremiumIsRequired()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            JsonResponse("""{"status":"fail","data":"RequiresPlayitPremium"}"""));

        TunnelCreateResult result = await apiClient.CreateHttpTunnelAsync("pocketmc-remote", 25580);

        Assert.False(result.Success);
        Assert.True(result.RequiresPlayitPremium);
        Assert.Contains("Premium", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private static string TunnelListJson(string originBody, string connectAddresses, string tunnelType = "minecraft-java")
    {
        return $$"""
        {
          "status": "success",
          "data": {
            "tunnels": [
              {
                "id": "tunnel-1",
                "name": "server",
                "tunnel_type": "{{tunnelType}}",
                "user_enabled": true,
                "connect_addresses": {{connectAddresses}},
                "public_allocations": [],
                "origin": { "type": "agent", {{originBody.Trim().TrimStart('{').TrimEnd('}')}} }
              }
            ]
          }
        }
        """;
    }
}
