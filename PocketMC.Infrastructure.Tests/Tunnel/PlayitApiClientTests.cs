using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Infrastructure.Tunnel;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Tunnel;

public class PlayitApiClientTests : IDisposable
{
    private readonly string _tempSettingsFile;
    private readonly ApplicationState _appState;
    private readonly SettingsManager _settingsManager;

    public PlayitApiClientTests()
    {
        _tempSettingsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-settings.json");
        _appState = new ApplicationState();
        _appState.Settings.PlayitConfigDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _settingsManager = new SettingsManager(_tempSettingsFile, NullLogger<SettingsManager>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempSettingsFile))
            {
                File.Delete(_tempSettingsFile);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private PlayitApiClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sender)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(sender));
        return new PlayitApiClient(_appState, _settingsManager, NullLogger<PlayitApiClient>.Instance, httpClient);
    }

    [Fact]
    public async Task GetTunnelsAsync_WhenNotConnected_ReturnsRequiresClaim()
    {
        _appState.Settings.PlayitPartnerConnection = null;
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await client.GetTunnelsAsync();

        Assert.False(result.Success);
        Assert.True(result.RequiresClaim);
        Assert.Contains("not connected", result.ErrorMessage);
    }

    [Fact]
    public async Task GetTunnelsAsync_WhenApiReturnsUnauthorized_ReturnsTokenInvalid()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection { AgentSecretKey = "dummy-key" };
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await client.GetTunnelsAsync();

        Assert.False(result.Success);
        Assert.True(result.IsTokenInvalid);
        Assert.Contains("rejected", result.ErrorMessage);
    }

    [Fact]
    public async Task GetTunnelsAsync_WhenApiSucceeds_ParsesTunnelsCorrectly()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection { AgentSecretKey = "dummy-key" };
        
        var jsonResponse = """
        {
            "status": "success",
            "data": {
                "tunnels": [
                    {
                        "id": "tunnel-123",
                        "name": "java-server",
                        "tunnel_type": "minecraft-java",
                        "user_enabled": true,
                        "connect_addresses": [
                            {
                                "type": "domain",
                                "value": "test.playit.gg:25565"
                            }
                        ],
                        "origin": {
                            "type": "agent",
                            "details": {
                                "agent_id": "agent-xyz",
                                "config": {
                                    "fields": [
                                        { "name": "local_port", "value": "25565" },
                                        { "name": "local_ip", "value": "127.0.0.1" }
                                    ]
                                }
                            }
                        },
                        "public_allocations": [
                            {
                                "type": "port",
                                "details": {
                                    "ip": "147.185.221.1",
                                    "port": 25565
                                }
                            }
                        ]
                    }
                ]
            }
        }
        """;

        var client = CreateClient((req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.playit.gg/v1/tunnels/list", req.RequestUri?.ToString());
            Assert.Equal("Agent-Key dummy-key", req.Headers.Authorization?.ToString());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            });
        });

        var result = await client.GetTunnelsAsync();

        Assert.True(result.Success);
        Assert.Single(result.Tunnels);
        
        var tunnel = result.Tunnels[0];
        Assert.Equal("tunnel-123", tunnel.Id);
        Assert.Equal("java-server", tunnel.Name);
        Assert.Equal("minecraft-java", tunnel.TunnelType);
        Assert.True(tunnel.IsEnabled);
        Assert.Equal("test.playit.gg:25565", tunnel.PublicAddress);
        Assert.Equal("147.185.221.1:25565", tunnel.NumericAddress);
        Assert.Equal(25565, tunnel.Port);
        Assert.True(tunnel.HasAgentOrigin);
        Assert.Equal("agent-xyz", tunnel.AgentId);
        Assert.Equal("127.0.0.1", tunnel.LocalIp);
    }

    [Fact]
    public async Task CreateTunnelAsync_WhenApiSucceeds_ReturnsTunnelId()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection { AgentSecretKey = "dummy-key", AgentId = "agent-xyz" };
        
        var jsonResponse = """
        {
            "status": "success",
            "data": {
                "id": "new-tunnel-456"
            }
        }
        """;

        var client = CreateClient(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.playit.gg/v1/tunnels/create", req.RequestUri?.ToString());
            
            string contentBody = await req.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(contentBody);
            Assert.Equal("java-tunnel", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("minecraft-java", doc.RootElement.GetProperty("protocol").GetProperty("details").GetString());
            Assert.Equal(25565, int.Parse(doc.RootElement.GetProperty("origin").GetProperty("data").GetProperty("config").GetProperty("fields")[0].GetProperty("value").GetString()!));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
        });

        var result = await client.CreateTunnelAsync("java-tunnel", "minecraft-java", 25565);

        Assert.True(result.Success);
        Assert.Equal("new-tunnel-456", result.TunnelId);
    }

    [Fact]
    public async Task CreateTunnelAsync_WhenApiLimitReached_ReturnsLimitError()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection { AgentSecretKey = "dummy-key", AgentId = "agent-xyz" };
        
        var jsonResponse = """
        {
            "status": "fail",
            "data": "RequiresPlayitPremium"
        }
        """;

        var client = CreateClient((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            });
        });

        var result = await client.CreateTunnelAsync("java-tunnel", "minecraft-java", 25565);

        Assert.False(result.Success);
        Assert.True(result.IsLimitError);
        Assert.True(result.RequiresPlayitPremium);
        Assert.Equal("RequiresPlayitPremium", result.ErrorCode);
        Assert.Contains("limit reached", result.ErrorMessage);
    }

    [Fact]
    public async Task EnableTunnelAsync_WhenApiSucceeds_ReturnsOk()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection { AgentSecretKey = "dummy-key" };
        
        var jsonResponse = """
        {
            "status": "success",
            "data": null
        }
        """;

        var client = CreateClient(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.playit.gg/tunnels/enable", req.RequestUri?.ToString());
            
            string contentBody = await req.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(contentBody);
            Assert.Equal("tunnel-123", doc.RootElement.GetProperty("tunnel_id").GetString());
            Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
        });

        var result = await client.EnableTunnelAsync("tunnel-123", true);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DeleteTunnelAsync_WhenApiSucceeds_ReturnsOk()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection { AgentSecretKey = "dummy-key" };
        
        var jsonResponse = """
        {
            "status": "success",
            "data": null
        }
        """;

        var client = CreateClient(async (req, ct) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.playit.gg/tunnels/delete", req.RequestUri?.ToString());
            
            string contentBody = await req.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(contentBody);
            Assert.Equal("tunnel-123", doc.RootElement.GetProperty("tunnel_id").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
        });

        var result = await client.DeleteTunnelAsync("tunnel-123");

        Assert.True(result.Success);
    }
}

internal sealed class DelegateHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sender;

    public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sender)
    {
        _sender = sender;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _sender(request, cancellationToken);
    }
}
