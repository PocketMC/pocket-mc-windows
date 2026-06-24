using System.Net;
using System.Text.Json;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Tunnel;

namespace PocketMC.Desktop.Tests;

public sealed class TunnelServiceTests
{
    [Fact]
    public async Task ResolveTunnelAsync_WhenAgentIsOffline_ReturnsPlayitAgentOffline()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            25565,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            displayName: "Java server"));
        PortCheckResult? portResult = result.ToPortCheckResult(new PortCheckRequest(25565));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.AgentOffline, result.Status);
        Assert.Equal(PortFailureCode.PlayitAgentOffline, result.FailureCode);
        Assert.NotNull(portResult);
        Assert.Equal(PortFailureCode.PlayitAgentOffline, portResult.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_WhenTokenIsInvalid_ReturnsStructuredPlayitFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            25565,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            displayName: "Java server"));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.True(result.IsTokenInvalid);
        Assert.Equal(PortFailureCode.PlayitTokenInvalid, result.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_WhenNoSecretIsAvailable_ReturnsClaimRequired()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            19132,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            displayName: "Bedrock server"));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.True(result.RequiresClaim);
        Assert.Equal(PortFailureCode.PlayitClaimRequired, result.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_WhenTunnelLimitIsReached_ReturnsTunnelLimitFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "status": "fail",
                          "data": "RequiresPlayitPremium"
                        }
                        """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "status": "success",
                      "data": {
                        "tunnels": [
                          {
                            "id": "1",
                            "name": "a",
                            "tunnel_type": "minecraft-java",
                            "connect_addresses": [{ "type": "domain", "value": { "address": "a.example.com:1" } }],
                            "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.1", "port": 1 } }],
                            "origin": { "type": "agent", "details": { "config_data": { "fields": [{ "name": "local_port", "value": "25565" }] } } }
                          },
                          {
                            "id": "2",
                            "name": "b",
                            "tunnel_type": "minecraft-bedrock",
                            "connect_addresses": [{ "type": "domain", "value": { "address": "b.example.com:2" } }],
                            "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.2", "port": 2 } }],
                            "origin": { "type": "agent", "details": { "config_data": { "fields": [{ "name": "local_port", "value": "19132" }] } } }
                          },
                          {
                            "id": "3",
                            "name": "c",
                            "tunnel_type": "tcp",
                            "connect_addresses": [{ "type": "domain", "value": { "address": "c.example.com:3" } }],
                            "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.3", "port": 3 } }],
                            "origin": { "type": "agent", "details": { "config_data": { "fields": [{ "name": "local_port", "value": "25566" }] } } }
                          },
                          {
                            "id": "4",
                            "name": "d",
                            "tunnel_type": "udp",
                            "connect_addresses": [{ "type": "domain", "value": { "address": "d.example.com:4" } }],
                            "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.4", "port": 4 } }],
                            "origin": { "type": "agent", "details": { "config_data": { "fields": [{ "name": "local_port", "value": "19133" }] } } }
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            25570,
            PortProtocol.Tcp,
            PortIpMode.IPv4,
            displayName: "Java server"));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.LimitReached, result.Status);
        Assert.Equal(PortFailureCode.TunnelLimitReached, result.FailureCode);
        Assert.Equal(4, result.ExistingTunnels.Count);
    }

    [Fact]
    public async Task PollForNewTunnelResultAsync_WhenItTimesOut_ReturnsPublicReachabilityFailure()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient();
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.PollForNewTunnelResultAsync(
            new PortCheckRequest(19140, PortProtocol.Udp, PortIpMode.IPv4, displayName: "Geyser Bedrock"),
            CancellationToken.None,
            timeout: TimeSpan.Zero);

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.Equal(PortFailureCode.PublicReachabilityFailure, result.FailureCode);
    }

    [Fact]
    public async Task ResolveTunnelAsync_ForSimpleVoiceChat_CreatesNativePlayitPayload()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        string? createPayload = null;
        int listCalls = 0;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                using var reader = new StreamReader(req.Content!.ReadAsStream());
                createPayload = reader.ReadToEnd();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"success","data":{"id":"voice"}}""")
                };
            }

            listCalls++;
            if (listCalls == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"success","data":{"tunnels":[]}}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "status": "success",
                      "data": {
                        "tunnels": [
                          {
                            "id": "voice",
                            "name": "voice",
                            "tunnel_type": "mc-simple-voice-chat",
                            "connect_addresses": [{ "type": "domain", "value": { "address": "voice.example.com:30000" } }],
                            "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.5", "port": 30000 } }],
                            "origin": { "type": "agent", "details": { "config_data": { "fields": [{ "name": "local_port", "value": "24454" }] } } }
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.AutoCreated, result.Status);
        Assert.NotNull(createPayload);
        using JsonDocument doc = JsonDocument.Parse(createPayload!);
        JsonElement root = doc.RootElement;
        JsonElement protocol = root.GetProperty("protocol");
        Assert.Equal("tunnel-type", protocol.GetProperty("type").GetString());
        Assert.Equal("mc-simple-voice-chat", protocol.GetProperty("details").GetString());
        Assert.DoesNotContain("raw-ports", createPayload);
        Assert.DoesNotContain("minecraft-bedrock", createPayload);
    }

    [Fact]
    public async Task ResolveTunnelAsync_ForSimpleVoiceChat_DoesNotCreateMinecraftBedrockOrRawUdpTunnel()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        string? createPayload = null;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                using var reader = new StreamReader(req.Content!.ReadAsStream());
                createPayload = reader.ReadToEnd();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"fail","data":"RequiresPlayitPremium"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"success","data":{"tunnels":[]}}""")
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        await service.ResolveTunnelAsync(new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat));

        Assert.NotNull(createPayload);
        Assert.DoesNotContain("minecraft-bedrock", createPayload);
        Assert.DoesNotContain("raw-ports", createPayload);
        Assert.Contains("mc-simple-voice-chat", createPayload);
    }

    [Fact]
    public async Task ResolveTunnelAsync_ForSimpleVoiceChat_ReusesExistingNativeTunnel()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        bool createCalled = false;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                createCalled = true;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "status": "success",
                      "data": {
                        "tunnels": [
                          {
                            "id": "voice",
                            "name": "voice",
                            "tunnel_type": "mc-simple-voice-chat",
                            "user_enabled": true,
                            "connect_addresses": [{ "type": "domain", "value": { "address": "voice.example.com:30000" } }],
                            "public_allocations": [{ "type": "PortAllocation", "details": { "ip": "10.0.0.5", "port": 30000 } }],
                            "origin": { "type": "agent", "details": { "agent_id": "test-agent", "config_data": { "fields": [{ "name": "local_port", "value": "24454" }] } } }
                          }
                        ]
                      }
                    }
                    """)
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Found, result.Status);
        Assert.Equal("voice.example.com:30000", result.PublicAddress);
        Assert.False(createCalled);
    }

    [Fact]
    public void FindTunnelForRequest_DoesNotMistakeJavaOrBedrockTunnelForSimpleVoiceChat()
    {
        var request = new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat);
        var tunnels = new List<TunnelData>
        {
            new() { Id = "java", Port = 24454, PublicAddress = "java.example.com", Protocol = PortProtocol.Tcp, TunnelType = "minecraft-java", IsEnabled = true },
            new() { Id = "bedrock", Port = 24454, PublicAddress = "bedrock.example.com", Protocol = PortProtocol.Udp, TunnelType = "minecraft-bedrock", IsEnabled = true }
        };

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(tunnels, request);

        Assert.Null(match);
    }

    [Fact]
    public void FindTunnelForRequest_SimpleVoiceChatWrongPortIsNotReused()
    {
        var request = new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat);
        var tunnels = new List<TunnelData>
        {
            new() { Id = "voice", Port = 24455, PublicAddress = "voice.example.com", Protocol = PortProtocol.Udp, TunnelType = "mc-simple-voice-chat", IsEnabled = true }
        };

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(tunnels, request);

        Assert.Null(match);
    }

    [Fact]
    public void FindTunnelForRequest_SimpleVoiceChatDisabledTunnelIsNotReused()
    {
        var request = new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat);
        var tunnels = new List<TunnelData>
        {
            new() { Id = "voice", Port = 24454, PublicAddress = "voice.example.com", Protocol = PortProtocol.Udp, TunnelType = "mc-simple-voice-chat", IsEnabled = false }
        };

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(tunnels, request);

        Assert.Null(match);
    }

    [Fact]
    public void FindTunnelForRequest_SimpleVoiceChatPendingAllocationIsNotReused()
    {
        var request = new PortCheckRequest(
            24454,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat);
        var tunnels = new List<TunnelData>
        {
            new() { Id = "voice", Port = 24454, PublicAddress = "", Protocol = PortProtocol.Udp, TunnelType = "mc-simple-voice-chat", IsEnabled = true }
        };

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(tunnels, request);

        Assert.Null(match);
    }

    [Fact]
    public async Task ResolveTunnelAsync_ForSimpleVoiceChat_DisabledExistingTunnelIsReportedNotDuplicated()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        bool createCalled = false;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                createCalled = true;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(VoiceTunnelJson("voice", 24454, enabled: false, publicAddress: "voice.example.com:30000"))
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(SimpleVoiceChatRequest(24454));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(createCalled);
    }

    [Fact]
    public async Task ResolveTunnelAsync_ForSimpleVoiceChat_PendingAllocationIsPolledNotDuplicated()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        int listCalls = 0;
        bool createCalled = false;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                createCalled = true;
            }

            listCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listCalls < 3
                    ? VoiceTunnelJson("voice", 24454, enabled: true, publicAddress: "")
                    : VoiceTunnelJson("voice", 24454, enabled: true, publicAddress: "voice.example.com:30000"))
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(SimpleVoiceChatRequest(24454));

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Found, result.Status);
        Assert.Equal("voice.example.com:30000", result.PublicAddress);
        Assert.True(listCalls >= 3);
        Assert.False(createCalled);
    }

    [Fact]
    public async Task ResolveTunnelAsync_ForSimpleVoiceChat_WrongPortIsReportedClearlyBeforeCreation()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        bool createCalled = false;
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("tunnels/create") == true)
            {
                createCalled = true;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(VoiceTunnelJson("voice", 24455, enabled: true, publicAddress: "voice.example.com:30000"))
            };
        });
        PlayitAgentHarness harness = workspace.CreatePlayitAgentHarness();
        harness.StateMachine.TransitionTo(PlayitAgentState.Connected);
        TunnelService service = workspace.CreateTunnelService(apiClient, harness.Service);

        TunnelResolutionResult result = await service.ResolveTunnelAsync(SimpleVoiceChatRequest(24454), allowAutoCreate: false);

        Assert.Equal(TunnelResolutionResult.TunnelStatus.Error, result.Status);
        Assert.Contains("24455", result.ErrorMessage);
        Assert.Contains("24454", result.ErrorMessage);
        Assert.False(createCalled);
    }

    [Fact]
    public async Task GetTunnelsAsync_UnknownTunnelTypesDoNotCrashDeserialization()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        workspace.WritePlayitSecret();
        PlayitApiClient apiClient = workspace.CreatePlayitApiClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "status": "success",
                      "data": {
                        "tunnels": [
                          {
                            "id": "unknown",
                            "name": "unknown",
                            "tunnel_type": "brand-new-future-type",
                            "user_enabled": true,
                            "connect_addresses": [],
                            "public_allocations": [],
                            "origin": { "type": "agent", "details": { "config_data": { "fields": [{ "name": "local_port", "value": "12345" }] } } }
                          }
                        ]
                      }
                    }
                    """)
            });

        TunnelListResult result = await apiClient.GetTunnelsAsync();

        Assert.True(result.Success);
        TunnelData tunnel = Assert.Single(result.Tunnels);
        Assert.Equal("brand-new-future-type", tunnel.TunnelType);
    }

    [Fact]
    public void FindTunnelForRequest_UsesProtocolAwareMatching()
    {
        var request = new PortCheckRequest(19132, PortProtocol.Udp, PortIpMode.IPv4, displayName: "Bedrock server");
        var tunnels = new List<TunnelData>
        {
            new() { Id = "java", Port = 19132, PublicAddress = "java.example.com", Protocol = PortProtocol.Tcp },
            new() { Id = "bedrock", Port = 19132, PublicAddress = "bedrock.example.com", Protocol = PortProtocol.Udp }
        };

        TunnelData? match = PlayitApiClient.FindTunnelForRequest(tunnels, request);

        Assert.NotNull(match);
        Assert.Equal("bedrock", match.Id);
    }

    private static PortCheckRequest SimpleVoiceChatRequest(int port)
    {
        return new PortCheckRequest(
            port,
            PortProtocol.Udp,
            PortIpMode.IPv4,
            bindingRole: PortBindingRole.SimpleVoiceChat,
            engine: PortEngine.SimpleVoiceChat);
    }

    private static string VoiceTunnelJson(string id, int port, bool enabled, string publicAddress)
    {
        string addresses = string.IsNullOrWhiteSpace(publicAddress)
            ? "[]"
            : $$"""[{ "type": "domain", "value": { "address": "{{publicAddress}}" } }]""";

        string allocations = string.IsNullOrWhiteSpace(publicAddress)
            ? "[]"
            : """[{ "type": "PortAllocation", "details": { "ip": "10.0.0.5", "port": 30000 } }]""";

        return $$"""
        {
          "status": "success",
          "data": {
            "tunnels": [
              {
                "id": "{{id}}",
                "name": "{{id}}",
                "tunnel_type": "mc-simple-voice-chat",
                "user_enabled": {{enabled.ToString().ToLowerInvariant()}},
                "connect_addresses": {{addresses}},
                "public_allocations": {{allocations}},
                "origin": { "type": "agent", "details": { "agent_id": "test-agent", "config_data": { "fields": [{ "name": "local_port", "value": "{{port}}" }] } } }
              }
            ]
          }
        }
        """;
    }
}


