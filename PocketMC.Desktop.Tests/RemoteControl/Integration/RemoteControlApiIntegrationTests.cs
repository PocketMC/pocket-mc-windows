using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.RemoteControl.Auth;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl.Integration;

public sealed class RemoteControlApiIntegrationTests : IAsyncLifetime
{
    private readonly ApplicationState _state;
    private readonly Mock<IServerLifecycleService> _lifecycleMock;
    private readonly RemoteAuthService _authService;
    private readonly RemoteDashboardHost _host;
    private readonly HttpClient _client;
    private readonly int _port;
    private string _pairingToken = default!;
    private string _deviceToken = default!;
    private string _deviceId = default!;

    public RemoteControlApiIntegrationTests()
    {
        _port = 50000 + Random.Shared.Next(10000);
        _state = new ApplicationState();
        _state.Settings.RemoteControl.Enabled = true;
        _state.Settings.RemoteControl.Port = _port;
        _state.Settings.RemoteControl.AccessMode = RemoteAccessMode.LanOnly;
        _state.Settings.RemoteControl.AllowRemoteConsoleCommands = true;
        _state.Settings.RemoteControl.AllowRemotePlayerActions = true;

        var tokenHasher = new RemoteTokenHasher();
        var settingsManager = new SettingsManager(Path.GetTempFileName(), NullLogger<SettingsManager>.Instance);
        _authService = new RemoteAuthService(_state, settingsManager, tokenHasher);

        _lifecycleMock = new Mock<IServerLifecycleService>();
        _lifecycleMock.Setup(x => x.IsRunning(It.IsAny<Guid>())).Returns(true);

        var statusService = new RemoteStatusService(null!, _lifecycleMock.Object, null!, null!, _state, null!);
        var instanceControlService = new RemoteInstanceControlService(null!, _lifecycleMock.Object);
        var auditLogService = new RemoteAuditLogService();
        var playerActionService = new RemotePlayerActionService(_state, null!, _lifecycleMock.Object, auditLogService);
        var wsHandler = new RemoteConsoleWebSocketHandler(_lifecycleMock.Object);
        var requestLimiter = new RemoteRequestLimiter();
        
        var tunnelManager = new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>());
        var localNetworkAddressService = new LocalNetworkAddressService();

        _host = new RemoteDashboardHost(
            _state,
            _authService,
            statusService,
            instanceControlService,
            playerActionService,
            wsHandler,
            auditLogService,
            requestLimiter,
            _lifecycleMock.Object,
            tunnelManager,
            localNetworkAddressService,
            NullLogger<RemoteDashboardHost>.Instance);

        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}")
        };
    }

    public async Task InitializeAsync()
    {
        await _host.StartAsync();

        // Setup a valid device
        var session = _authService.CreatePairingSession(TimeSpan.FromMinutes(1));
        _pairingToken = session.Token;
        var exchange = _authService.ExchangePairingToken(_pairingToken, "TestDevice");
        _deviceToken = exchange.DeviceToken!;
        _deviceId = exchange.DeviceId!;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
    }

    [Fact]
    public async Task GetStatus_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_WithAuth_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deviceToken);
        var response = await _client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("hostRunning", json);
    }

    [Fact]
    public async Task GetStatus_WithRevokedAuth_Returns401()
    {
        _authService.RevokeDevice(_deviceId);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deviceToken);
        var response = await _client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConsoleCommand_WhenDisabled_Returns403()
    {
        _state.Settings.RemoteControl.AllowRemoteConsoleCommands = false;
        var instanceId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/instances/{instanceId}/console/command");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deviceToken);
        request.Content = JsonContent.Create(new { command = "help" });

        var response = await _client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Expected Forbidden, got {response.StatusCode}. Content: {content}");
    }

    [Fact]
    public async Task WebSocketTicket_ReturnsShortLivedTicket()
    {
        var instanceId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/instances/{instanceId}/console/ticket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deviceToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ticket = json.GetProperty("ticket").GetString();
        Assert.NotNull(ticket);

        // Validate the ticket
        bool valid = _authService.ValidateWebSocketTicket(ticket, instanceId, out string deviceId);
        Assert.True(valid);
        Assert.Equal(_deviceId, deviceId);

        // Ticket should be single-use
        bool validSecondTime = _authService.ValidateWebSocketTicket(ticket, instanceId, out _);
        Assert.False(validSecondTime);
    }

    [Fact]
    public async Task WebSocketEndpoint_RequiresValidTicket()
    {
        var instanceId = Guid.NewGuid();
        
        // 1. No ticket -> 401
        var response1 = await _client.GetAsync($"/ws/instances/{instanceId}/console");
        Assert.Equal(HttpStatusCode.Unauthorized, response1.StatusCode);

        // 2. Invalid ticket -> 401
        var response2 = await _client.GetAsync($"/ws/instances/{instanceId}/console?ticket=invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, response2.StatusCode);
    }
}
