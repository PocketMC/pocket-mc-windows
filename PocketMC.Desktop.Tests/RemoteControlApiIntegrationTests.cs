using PocketMC.Desktop.Features.RemoteControl.Models;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl.Integration;

public sealed class RemoteControlApiIntegrationTests : IAsyncLifetime
{
    private readonly ApplicationState _state;
    private readonly Mock<IServerLifecycleService> _lifecycleMock;
    private readonly RemoteDashboardHost _host;
    private readonly HttpClient _client;
    private readonly int _port;

    public RemoteControlApiIntegrationTests()
    {
        _port = GetAvailableTcpPort();
        _state = new ApplicationState();
        _state.Settings.RemoteControl.Enabled = true;
        _state.Settings.RemoteControl.Port = _port;
        _state.Settings.RemoteControl.AccessMode = RemoteAccessMode.LanOnly;
        _state.Settings.RemoteControl.AllowRemoteConsoleCommands = true;
        _state.Settings.RemoteControl.AllowRemotePlayerActions = true;
        _state.Settings.RemoteControl.RequireAuthentication = false;

        _lifecycleMock = new Mock<IServerLifecycleService>();
        _lifecycleMock.Setup(x => x.IsRunning(It.IsAny<Guid>())).Returns(true);

        var statusService = new RemoteStatusService(null!, _lifecycleMock.Object, null!, null!, _state, null!, new PocketMC.Desktop.Helpers.GeyserDetector());
        var instanceControlService = new RemoteInstanceControlService(null!, _lifecycleMock.Object);
        var auditLogService = new RemoteAuditLogService();
        var playerActionService = new RemotePlayerActionService(_state, null!, _lifecycleMock.Object, auditLogService);
        var wsHandler = new RemoteConsoleWebSocketHandler(_lifecycleMock.Object);
        var requestLimiter = new RemoteRequestLimiter();

        var tunnelManager = new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>());
        var localNetworkAddressService = new LocalNetworkAddressService();
        var authService = new RemoteAuthenticationService();

        _host = new RemoteDashboardHost(
            _state,
            statusService,
            instanceControlService,
            playerActionService,
            wsHandler,
            auditLogService,
            requestLimiter,
            _lifecycleMock.Object,
            tunnelManager,
            localNetworkAddressService,
            authService,
            NullLogger<RemoteDashboardHost>.Instance);

        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}")
        };
        _client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }

    public async Task InitializeAsync()
    {
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task GetStatus_WithoutAuth_Returns200()
    {
        var response = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("hostRunning", json);
    }

    [Fact]
    public async Task ConsoleCommand_WhenDisabled_Returns403()
    {
        _state.Settings.RemoteControl.AllowRemoteConsoleCommands = false;
        var instanceId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/instances/{instanceId}/console/command");
        request.Content = JsonContent.Create(new { command = "help" });

        var response = await _client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Expected Forbidden, got {response.StatusCode}. Content: {content}");
    }

    [Fact]
    public async Task Login_RateLimiting_Returns429AfterFiveAttempts()
    {
        _state.Settings.RemoteControl.RequireAuthentication = true;
        _state.Settings.RemoteControl.PasswordHash = "some_hash";

        // Try to log in 5 times (should return 401 Unauthorized for incorrect passwords)
        for (int i = 0; i < 5; i++)
        {
            var loginRequest = new RemoteLoginRequest { Password = "wrong_password" };
            var response = await _client.PostAsJsonAsync("/api/login", loginRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // The 6th attempt should be rate limited and return 429 Too Many Requests
        var rateLimitedResponse = await _client.PostAsJsonAsync("/api/login", new RemoteLoginRequest { Password = "wrong_password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
    }
}



