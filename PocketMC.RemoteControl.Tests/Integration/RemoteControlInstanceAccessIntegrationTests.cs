using PocketMC.RemoteControl.Hosting;
using PocketMC.RemoteControl.Services;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Instances;
using PocketMC.Domain.Models;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Application.Interfaces;
using PocketMC.RemoteControl.Models;
using PocketMC.RemoteControl.Tunnels;

namespace PocketMC.RemoteControl.Tests.Integration;

public sealed class RemoteControlInstanceAccessIntegrationTests : IAsyncLifetime
{
    private readonly ApplicationState _state;
    private readonly Mock<IServerLifecycleService> _lifecycleMock;
    private readonly RemoteDashboardHost _host;
    private readonly HttpClient _client;
    private readonly int _port;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly RemoteAuthenticationService _authService;

    private readonly InstanceMetadata _instanceA;
    private readonly InstanceMetadata _instanceB;
    private readonly string _tempFolderA;
    private readonly string _tempFolderB;

    public RemoteControlInstanceAccessIntegrationTests()
    {
        _port = GetAvailableTcpPort();
        _state = new ApplicationState();
        _authService = new RemoteAuthenticationService();

        _instanceA = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Survival Server",
            ServerType = "Paper",
            MinecraftVersion = "1.21"
        };
        _instanceB = new InstanceMetadata
        {
            Id = Guid.NewGuid(),
            Name = "Creative Server",
            ServerType = "Purpur",
            MinecraftVersion = "1.21"
        };

        _tempFolderA = Path.Combine(Path.GetTempPath(), "PocketMC_Test_" + _instanceA.Id);
        _tempFolderB = Path.Combine(Path.GetTempPath(), "PocketMC_Test_" + _instanceB.Id);
        Directory.CreateDirectory(_tempFolderA);
        Directory.CreateDirectory(_tempFolderB);

        var pathService = new InstancePathService(_state);
        _instanceRegistry = new InstanceRegistry(pathService, NullLogger<InstanceRegistry>.Instance);
        _instanceRegistry.Register(_instanceA, _tempFolderA);
        _instanceRegistry.Register(_instanceB, _tempFolderB);

        _state.Settings.RemoteControl.Enabled = true;
        _state.Settings.RemoteControl.Port = _port;
        _state.Settings.RemoteControl.AccessMode = RemoteAccessMode.LanOnly;
        _state.Settings.RemoteControl.RequireAuthentication = true;
        _state.Settings.RemoteControl.Username = "admin";
        _state.Settings.RemoteControl.PasswordHash = _authService.HashPassword("adminpass");

        // Subuser 1: AllowAllInstances = true
        var allUser = new RemoteControlUser
        {
            Id = Guid.NewGuid().ToString(),
            Username = "allUser",
            PasswordHash = _authService.HashPassword("allpass"),
            AllowAllInstances = true,
            AllowRemoteConsoleCommands = true,
            AllowRemotePlayerActions = true,
            AllowRemoteServerSettings = true,
            AllowRemoteServerAddons = true,
            AllowRemoteFileManager = true,
            AllowRemoteServerBackups = true
        };

        // Subuser 2: AllowAllInstances = false, only InstanceA
        var instAUser = new RemoteControlUser
        {
            Id = Guid.NewGuid().ToString(),
            Username = "instAUser",
            PasswordHash = _authService.HashPassword("instApass"),
            AllowAllInstances = false,
            AllowedInstanceIds = new List<Guid> { _instanceA.Id },
            AllowRemoteConsoleCommands = true,
            AllowRemotePlayerActions = true,
            AllowRemoteServerSettings = true,
            AllowRemoteServerAddons = true,
            AllowRemoteFileManager = true,
            AllowRemoteServerBackups = true
        };

        // Subuser 3: AllowAllInstances = false, no instances
        var noInstUser = new RemoteControlUser
        {
            Id = Guid.NewGuid().ToString(),
            Username = "noInstUser",
            PasswordHash = _authService.HashPassword("nopass"),
            AllowAllInstances = false,
            AllowedInstanceIds = new List<Guid>(),
            AllowRemoteConsoleCommands = true,
            AllowRemotePlayerActions = true,
            AllowRemoteServerSettings = true,
            AllowRemoteServerAddons = true,
            AllowRemoteFileManager = true,
            AllowRemoteServerBackups = true
        };

        _state.Settings.RemoteControl.Users = new List<RemoteControlUser> { allUser, instAUser, noInstUser };

        _lifecycleMock = new Mock<IServerLifecycleService>();
        _lifecycleMock.Setup(x => x.IsRunning(It.IsAny<Guid>())).Returns(true);

        var resourceMock = new Mock<IResourceMonitorService>();
        resourceMock.Setup(x => x.Metrics).Returns(new System.Collections.Concurrent.ConcurrentDictionary<Guid, InstanceMetrics>());

        var serverStateFileService = new PocketMC.Infrastructure.Players.ServerStateFileService(
            _instanceRegistry,
            NullLogger<PocketMC.Infrastructure.Players.ServerStateFileService>.Instance);

        var statusService = new RemoteStatusService(
            _instanceRegistry,
            _lifecycleMock.Object,
            resourceMock.Object,
            new LocalNetworkAddressService(),
            _state,
            serverStateFileService,
            new PocketMC.Infrastructure.Instances.GeyserDetector(new PocketMC.Infrastructure.Marketplace.AddonManifestService()));

        var instanceControlService = new RemoteInstanceControlService(_instanceRegistry, _lifecycleMock.Object);
        var auditLogService = new RemoteAuditLogService();
        var playerActionService = new RemotePlayerActionService(_state, _instanceRegistry, _lifecycleMock.Object, auditLogService);
        var wsHandler = new RemoteConsoleWebSocketHandler(_lifecycleMock.Object);
        var requestLimiter = new RemoteRequestLimiter();
        var tunnelManager = new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>());
        var localNetworkAddressService = new LocalNetworkAddressService();

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
            _authService,
            NullLogger<RemoteDashboardHost>.Instance,
            _instanceRegistry,
            null,
            null);

        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookieContainer };
        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}")
        };
    }

    public async Task InitializeAsync()
    {
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();

        try { Directory.Delete(_tempFolderA, true); } catch { }
        try { Directory.Delete(_tempFolderB, true); } catch { }
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task LoginAsync(string username, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/login", new RemoteLoginRequest
        {
            Username = username,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAccessAllInstances()
    {
        await LoginAsync("admin", "adminpass");

        var response = await _client.GetAsync("/api/instances");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var instances = await response.Content.ReadFromJsonAsync<List<RemoteInstanceDto>>();
        Assert.NotNull(instances);
        Assert.Equal(2, instances.Count);

        var statusA = await _client.GetAsync($"/api/instances/{_instanceA.Id}/status");
        Assert.Equal(HttpStatusCode.OK, statusA.StatusCode);

        var statusB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/status");
        Assert.Equal(HttpStatusCode.OK, statusB.StatusCode);
    }

    [Fact]
    public async Task SubUser_WithAllowAllInstances_CanAccessAllInstances()
    {
        await LoginAsync("allUser", "allpass");

        var response = await _client.GetAsync("/api/instances");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var instances = await response.Content.ReadFromJsonAsync<List<RemoteInstanceDto>>();
        Assert.NotNull(instances);
        Assert.Equal(2, instances.Count);

        var statusA = await _client.GetAsync($"/api/instances/{_instanceA.Id}/status");
        Assert.Equal(HttpStatusCode.OK, statusA.StatusCode);

        var statusB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/status");
        Assert.Equal(HttpStatusCode.OK, statusB.StatusCode);
    }

    [Fact]
    public async Task SubUser_WithRestrictedInstances_OnlySeesAndAccessesAllowedInstance()
    {
        await LoginAsync("instAUser", "instApass");

        // 1. /api/instances should only return InstanceA
        var response = await _client.GetAsync("/api/instances");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var instances = await response.Content.ReadFromJsonAsync<List<RemoteInstanceDto>>();
        Assert.NotNull(instances);
        Assert.Single(instances);
        Assert.Equal(_instanceA.Id, instances[0].Id);

        // 2. Allowed instance endpoints work
        var statusA = await _client.GetAsync($"/api/instances/{_instanceA.Id}/status");
        Assert.Equal(HttpStatusCode.OK, statusA.StatusCode);

        // 3. Unauthorized InstanceB endpoints return 403 Forbidden
        var statusB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/status");
        Assert.Equal(HttpStatusCode.Forbidden, statusB.StatusCode);

        var startB = await _client.PostAsync($"/api/instances/{_instanceB.Id}/start", null);
        Assert.Equal(HttpStatusCode.Forbidden, startB.StatusCode);

        var stopB = await _client.PostAsync($"/api/instances/{_instanceB.Id}/stop", null);
        Assert.Equal(HttpStatusCode.Forbidden, stopB.StatusCode);

        var restartB = await _client.PostAsync($"/api/instances/{_instanceB.Id}/restart", null);
        Assert.Equal(HttpStatusCode.Forbidden, restartB.StatusCode);

        var consoleHistoryB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/console/history");
        Assert.Equal(HttpStatusCode.Forbidden, consoleHistoryB.StatusCode);

        var consoleCmdB = await _client.PostAsJsonAsync($"/api/instances/{_instanceB.Id}/console/command", new RemoteCommandRequest { Command = "say hello" });
        Assert.Equal(HttpStatusCode.Forbidden, consoleCmdB.StatusCode);

        var playersB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/players");
        Assert.Equal(HttpStatusCode.Forbidden, playersB.StatusCode);

        var kickB = await _client.PostAsJsonAsync($"/api/instances/{_instanceB.Id}/players/Steve/kick", new RemotePlayerActionRequest());
        Assert.Equal(HttpStatusCode.Forbidden, kickB.StatusCode);

        var propsB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/properties");
        Assert.Equal(HttpStatusCode.Forbidden, propsB.StatusCode);

        var addonsB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/addons");
        Assert.Equal(HttpStatusCode.Forbidden, addonsB.StatusCode);

        var filesB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/files");
        Assert.Equal(HttpStatusCode.Forbidden, filesB.StatusCode);

        var backupsB = await _client.GetAsync($"/api/instances/{_instanceB.Id}/backups");
        Assert.Equal(HttpStatusCode.Forbidden, backupsB.StatusCode);
    }

    [Fact]
    public async Task SubUser_WithNoInstances_ReturnsEmptyInstancesList()
    {
        await LoginAsync("noInstUser", "nopass");

        var response = await _client.GetAsync("/api/instances");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var instances = await response.Content.ReadFromJsonAsync<List<RemoteInstanceDto>>();
        Assert.NotNull(instances);
        Assert.Empty(instances);

        var statusA = await _client.GetAsync($"/api/instances/{_instanceA.Id}/status");
        Assert.Equal(HttpStatusCode.Forbidden, statusA.StatusCode);
    }

    [Fact]
    public async Task StatusEndpoint_WhenUnauthenticated_ReturnsFalseForAdminPermissions()
    {
        var response = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<RemoteDashboardStatus>();
        Assert.NotNull(status);
        Assert.True(status.HostRunning);
        Assert.False(status.AllowRemoteConsoleCommands);
        Assert.False(status.AllowRemotePlayerActions);
        Assert.False(status.AllowRemoteServerSettings);
        Assert.False(status.AllowRemoteServerAddons);
        Assert.False(status.AllowRemoteFileManager);
        Assert.False(status.AllowRemoteServerBackups);
    }

    [Fact]
    public async Task FilesEndpoint_WhenPathEscapesInstanceRoot_ReturnsBadRequest()
    {
        await LoginAsync("admin", "adminpass");

        var response = await _client.GetAsync($"/api/instances/{_instanceA.Id}/files?path=../../windows/system32");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BackupEndpoint_WhenConcurrentBackupsTriggered_ReturnsConflictForDuplicate()
    {
        await LoginAsync("admin", "adminpass");

        var backupMock = new Mock<PocketMC.Infrastructure.Backups.BackupService>(null!, null!, null!, null!, null!);
        var tcs = new TaskCompletionSource<bool>();
        backupMock.Setup(b => b.RunBackupAsync(It.IsAny<InstanceMetadata>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Action<string>?>(), It.IsAny<IProgress<double>?>()))
            .Returns(async () => { await tcs.Task; });

        var localNet = new LocalNetworkAddressService();
        var hostWithBackup = new RemoteDashboardHost(
            _state,
            null!,
            null!,
            null!,
            null!,
            new RemoteAuditLogService(),
            new RemoteRequestLimiter(),
            _lifecycleMock.Object,
            new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>()),
            localNet,
            _authService,
            NullLogger<RemoteDashboardHost>.Instance,
            _instanceRegistry,
            null,
            backupMock.Object);

        int backupPort = GetAvailableTcpPort();
        _state.Settings.RemoteControl.Port = backupPort;
        await hostWithBackup.StartAsync();

        try
        {
            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler { CookieContainer = cookieContainer };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{backupPort}") };

            var loginRes = await client.PostAsJsonAsync("/api/login", new RemoteLoginRequest { Username = "admin", Password = "adminpass" });
            Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

            var firstCall = client.PostAsync($"/api/instances/{_instanceA.Id}/backups", null);
            await Task.Delay(100);
            var secondCall = await client.PostAsync($"/api/instances/{_instanceA.Id}/backups", null);

            Assert.Equal(HttpStatusCode.Conflict, secondCall.StatusCode);

            tcs.SetResult(true);
            var firstRes = await firstCall;
            Assert.Equal(HttpStatusCode.OK, firstRes.StatusCode);
        }
        finally
        {
            await hostWithBackup.StopAsync();
        }
    }
}
