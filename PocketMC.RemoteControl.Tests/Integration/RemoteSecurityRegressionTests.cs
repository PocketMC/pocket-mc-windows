using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Application.Interfaces;
using PocketMC.RemoteControl.Hosting;
using PocketMC.RemoteControl.Services;
using PocketMC.RemoteControl.Tunnels;

namespace PocketMC.RemoteControl.Tests.Integration;

public sealed class RemoteSecurityRegressionTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC-Remote-Security-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationState _state = new();
    private readonly RemoteAuthenticationService _auth = new();
    private readonly Mock<IServerLifecycleService> _lifecycle = new();
    private readonly Mock<IServerProcess> _process = new();
    private readonly RemoteControlUser _user;
    private readonly InstanceMetadata _metadata = new() { Id = Guid.NewGuid(), ServerType = "Paper", Name = "Restricted" };
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;
    private readonly RemoteDashboardHost _host;
    private readonly string _server;
    private readonly int _port;

    public RemoteSecurityRegressionTests()
    {
        _server = Path.Combine(_root, "alpha"); Directory.CreateDirectory(_server);
        _state.Settings.AppRootPath = _root;
        var settings = _state.Settings.RemoteControl;
        settings.Enabled = true; settings.RequireAuthentication = true; settings.AccessMode = RemoteAccessMode.LanOnly;
        settings.Username = "owner"; settings.PasswordHash = _auth.HashPassword("owner-password");
        using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); _port = ((IPEndPoint)listener.LocalEndpoint).Port; }
        settings.Port = _port;
        _user = new RemoteControlUser
        {
            Username = "restricted",
            PasswordHash = _auth.HashPassword("old-password"),
            AllowAllInstances = false,
            AllowedInstanceIds = new() { _metadata.Id },
            AllowRemoteServerAddons = true,
            AllowRemoteServerSettings = true,
            AllowRemoteConsoleCommands = true
        };
        settings.Users.Add(_user);
        var paths = new InstancePathService(_state);
        var registry = new InstanceRegistry(paths, NullLogger<InstanceRegistry>.Instance);
        registry.Register(_metadata, _server);
        var manager = new InstanceManager(registry, paths, _state, Mock.Of<IAssetProvider>(), NullLogger<InstanceManager>.Instance, Mock.Of<IServiceProvider>());
        var detector = new GeyserDetector(new PocketMC.Infrastructure.Marketplace.AddonManifestService());
        _process.SetupGet(p => p.OutputBuffer).Returns(Array.Empty<string>());
        _lifecycle.Setup(l => l.GetProcess(_metadata.Id)).Returns(_process.Object);
        var audit = new RemoteAuditLogService();
        var address = new PocketMC.RemoteControl.Services.LocalNetworkAddressService();
        _host = new RemoteDashboardHost(_state,
            new RemoteStatusService(registry, _lifecycle.Object, null!, address, _state, null!, detector),
            new RemoteInstanceControlService(registry, _lifecycle.Object),
            new RemotePlayerActionService(_state, registry, _lifecycle.Object, audit),
            new RemoteConsoleWebSocketHandler(_lifecycle.Object), audit, new RemoteRequestLimiter(), _lifecycle.Object,
            new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>()), address, _auth, NullLogger<RemoteDashboardHost>.Instance,
            registry, new ServerConfigurationService(manager, detector));
        _client = new HttpClient(new HttpClientHandler { CookieContainer = _cookies }) { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
    }
    public async Task InitializeAsync()
    {
        await _host.StartAsync();
        using var login = await _client.PostAsJsonAsync("/api/login", new { username = "restricted", password = "old-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
    public async Task DisposeAsync() { _client.Dispose(); await _host.StopAsync(); Directory.Delete(_root, true); }
    private string Route(string suffix) => $"/api/instances/{_metadata.Id}/{suffix}";

    [Theory]
    [InlineData("../alpha-private/victim.txt")]
    [InlineData("server.properties")]
    [InlineData(".")]
    [InlineData("plugins")]
    public async Task AddonUninstall_RejectsNonInventoryTargets(string target)
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha-private"));
        File.WriteAllText(Path.Combine(_root, "alpha-private/victim.txt"), "outside");
        File.WriteAllText(Path.Combine(_server, "server.properties"), "config");
        Directory.CreateDirectory(Path.Combine(_server, "plugins"));
        using var response = await _client.PostAsJsonAsync(Route("addons/uninstall"), new { addonPathOrId = target });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(_root, "alpha-private/victim.txt")));
        Assert.True(File.Exists(Path.Combine(_server, "server.properties")));
    }
    [Fact]
    public async Task Settings_CannotInjectProperties()
    {
        _user.AllowRemoteConsoleCommands = false;
        File.WriteAllText(Path.Combine(_server, "server.properties"), "motd=original\n");
        using var response = await _client.PutAsJsonAsync(Route("properties"), new { motd = "hello\nenable-rcon=true\nrcon.password=injected" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("motd=original\n", File.ReadAllText(Path.Combine(_server, "server.properties")));
    }
    [Fact]
    public async Task PasswordChange_RevokesExistingCookie()
    {
        _user.PasswordHash = _auth.HashPassword("new-password");
        using var response = await _client.GetAsync(Route("addons"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task Logout_RevokesCookieAndReturnsUnauthorized()
    {
        using var logout = await _client.PostAsync("/api/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var response = await _client.GetAsync(Route("addons"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task DeletedUser_CannotReceiveFurtherWebSocketLogs()
    {
        using var socket = new ClientWebSocket(); socket.Options.Cookies = _cookies;
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws/instances/{_metadata.Id}/console"), default);
        _state.Settings.RemoteControl.Users.Remove(_user);
        _process.Raise(p => p.OnOutputLine += null, "PRIVATE_AFTER_REVOCATION");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] data = new byte[4096];
        try
        {
            var received = await socket.ReceiveAsync(data, timeout.Token);
            Assert.DoesNotContain("PRIVATE_AFTER_REVOCATION", Encoding.UTF8.GetString(data, 0, received.Count));
        }
        catch (WebSocketException) { } // Revocation aborts the established connection.
        catch (OperationCanceledException) { Assert.Fail("Revoked console was not closed promptly."); }
    }
}
