using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Diagnostics;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Players.Services;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Tests;

internal sealed class PortReliabilityTestWorkspace : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public PortReliabilityTestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));
        PlayitConfigDirectory = Path.Combine(RootPath, "playit-config");

        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(PlayitConfigDirectory);

        AppState = new ApplicationState();
        AppState.ApplySettings(new AppSettings
        {
            AppRootPath = RootPath,
            PlayitConfigDirectory = PlayitConfigDirectory
        });

        PathService = new InstancePathService(AppState);
        Registry = new InstanceRegistry(PathService, NullLogger<InstanceRegistry>.Instance);
        InstanceManager = new InstanceManager(Registry, PathService, AppState, new EmptyAssetProvider(), NullLogger<InstanceManager>.Instance);
        ConfigurationService = new ServerConfigurationService(InstanceManager);
        SettingsManager = new SettingsManager();
    }

    public string RootPath { get; }

    public string PlayitConfigDirectory { get; }

    public ApplicationState AppState { get; }

    public InstancePathService PathService { get; }

    public InstanceRegistry Registry { get; }

    public InstanceManager InstanceManager { get; }

    public ServerConfigurationService ConfigurationService { get; }

    public SettingsManager SettingsManager { get; }

    public InstanceMetadata CreateInstance(
        string name,
        string serverType = "Paper",
        string minecraftVersion = "1.20.4")
    {
        return InstanceManager.CreateInstance(name, string.Empty, serverType, minecraftVersion);
    }

    public string GetInstancePath(Guid instanceId)
    {
        return Registry.GetPath(instanceId)
            ?? throw new DirectoryNotFoundException($"Could not find instance path for {instanceId}.");
    }

    public void SaveMetadata(InstanceMetadata metadata)
    {
        InstanceManager.SaveMetadata(metadata, GetInstancePath(metadata.Id));
    }

    public void WriteServerProperties(Guid instanceId, params string[] lines)
    {
        File.WriteAllLines(Path.Combine(GetInstancePath(instanceId), "server.properties"), lines);
    }

    public void WriteFile(Guid instanceId, string relativePath, string contents)
    {
        string fullPath = Path.Combine(GetInstancePath(instanceId), relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
    }

    public void WritePlayitSecret(string secret = "test-secret")
    {
        Directory.CreateDirectory(PlayitConfigDirectory);
        File.WriteAllText(Path.Combine(PlayitConfigDirectory, "playit.toml"), $"secret_key = \"{secret}\"");
    }

    public void EnsurePlayitBinaryExists()
    {
        string executablePath = AppState.GetPlayitExecutablePath();
        string? directory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(executablePath, "stub");
    }

    public PortProbeService CreatePortProbeService()
    {
        return new PortProbeService(NullLogger<PortProbeService>.Instance);
    }

    public PortLeaseRegistry CreatePortLeaseRegistry()
    {
        return new PortLeaseRegistry();
    }

    public PortRecoveryService CreatePortRecoveryService(PortProbeService? probeService = null, PortLeaseRegistry? leaseRegistry = null)
    {
        return new PortRecoveryService(
            probeService ?? CreatePortProbeService(),
            leaseRegistry ?? CreatePortLeaseRegistry(),
            NullLogger<PortRecoveryService>.Instance);
    }

    public ServerProcessManager CreateServerProcessManager()
    {
        var jobObject = Track(new JobObject());
        return new ServerProcessManager(
            jobObject,
            InstanceManager,
            Registry,
            CreateServerLaunchConfigurator(),
            new PlayerListParser(),
            NullLogger<ServerProcessManager>.Instance,
            NullLoggerFactory.Instance);
    }

    public PortPreflightService CreatePortPreflightService(ServerProcessManager? processManager = null)
    {
        return new PortPreflightService(
            Registry,
            ConfigurationService,
            processManager ?? CreateServerProcessManager(),
            AppState,
            NullLogger<PortPreflightService>.Instance);
    }

    public PlayitApiClient CreatePlayitApiClient(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        HttpClient httpClient = responder == null
            ? new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"success","data":{"tunnels":[]}}""")
                }))
            : new HttpClient(new DelegateHttpMessageHandler((request, _) => responder(request)));

        return new PlayitApiClient(
            AppState,
            SettingsManager,
            NullLogger<PlayitApiClient>.Instance,
            httpClient);
    }

    public PlayitAgentHarness CreatePlayitAgentHarness()
    {
        var processManager = new PlayitAgentProcessManager(
            Track(new JobObject()),
            NullLogger<PlayitAgentProcessManager>.Instance);
        var stateMachine = new PlayitAgentStateMachine();
        var toastService = new WindowsToastNotificationService(NullLogger<WindowsToastNotificationService>.Instance);
        var partnerClient = new PlayitPartnerProvisioningClient(
            AppState,
            SettingsManager,
            NullLogger<PlayitPartnerProvisioningClient>.Instance,
            new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "accountId": 1,
                          "agentId": "test-agent",
                          "agentSecretKey": "test-secret",
                          "agentOverLimit": false
                        }
                        """)
                })));
        var agentService = new PlayitAgentService(
            AppState,
            SettingsManager,
            processManager,
            stateMachine,
            partnerClient,
            toastService,
            CreateDownloaderService(),
            NullLogger<PlayitAgentService>.Instance);

        Track(agentService);
        return new PlayitAgentHarness(agentService, stateMachine);
    }

    public TunnelService CreateTunnelService(
        PlayitApiClient apiClient,
        PlayitAgentService agentService)
    {
        return new TunnelService(apiClient, agentService, NullLogger<TunnelService>.Instance);
    }

    public DependencyHealthMonitor CreateDependencyHealthMonitor(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var factory = new TestHttpClientFactory(_ =>
            new HttpClient(new DelegateHttpMessageHandler((request, _) =>
                responder?.Invoke(request) ??
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                })));

        return Track(new DependencyHealthMonitor(factory, NullLogger<DependencyHealthMonitor>.Instance));
    }

    public PortDiagnosticsSnapshotBuilder CreateDiagnosticsSnapshotBuilder(
        PortPreflightService preflightService,
        PortLeaseRegistry leaseRegistry,
        PortRecoveryService recoveryService,
        PlayitAgentService playitAgentService,
        PlayitApiClient playitApiClient,
        DependencyHealthMonitor dependencyHealthMonitor)
    {
        return new PortDiagnosticsSnapshotBuilder(
            AppState,
            Registry,
            preflightService,
            leaseRegistry,
            recoveryService,
            playitAgentService,
            playitApiClient,
            dependencyHealthMonitor,
            NullLogger<PortDiagnosticsSnapshotBuilder>.Instance);
    }

    public ServerLifecycleService CreateServerLifecycleService(
        ServerProcessManager processManager,
        PortPreflightService preflightService,
        PortProbeService probeService,
        PortLeaseRegistry leaseRegistry,
        PortRecoveryService recoveryService,
        INotificationService? notificationService = null)
    {
        return new ServerLifecycleService(
            processManager,
            Registry,
            preflightService,
            probeService,
            leaseRegistry,
            recoveryService,
            notificationService ?? new RecordingNotificationService(),
            NullLogger<ServerLifecycleService>.Instance,
            AppState,
            new PocketMC.Desktop.Features.Instances.Services.GeyserProvisioningService(null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<PocketMC.Desktop.Features.Instances.Services.GeyserProvisioningService>.Instance));
    }

    public int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public int GetAvailableUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables.AsEnumerable().Reverse())
        {
            disposable.Dispose();
        }

        if (!Directory.Exists(RootPath))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(RootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(RootPath, recursive: true);
    }

    private DownloaderService CreateDownloaderService()
    {
        return new DownloaderService(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                }))),
            NullLogger<DownloaderService>.Instance);
    }

    private ServerLaunchConfigurator CreateServerLaunchConfigurator()
    {
        var downloader = CreateDownloaderService();
        var validator = new JavaRuntimeValidator(NullLogger<JavaRuntimeValidator>.Instance);
        var adoptiumClient = new JavaAdoptiumClient(
            new TestHttpClientFactory(_ => new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                }))),
            NullLogger<JavaAdoptiumClient>.Instance);
        var javaProvisioning = new JavaProvisioningService(
            downloader,
            AppState,
            adoptiumClient,
            validator,
            SettingsManager,
            NullLogger<JavaProvisioningService>.Instance);
        var phpProvisioning = new PhpProvisioningService(
            new HttpClient(new DelegateHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                })),
            downloader,
            AppState,
            NullLogger<PhpProvisioningService>.Instance);

        var vanillaProvider = new VanillaProvider(
            new HttpClient(),
            AppState,
            downloader,
            NullLogger<VanillaProvider>.Instance);

        return new ServerLaunchConfigurator(
            javaProvisioning,
            phpProvisioning,
            vanillaProvider,
            NullLogger<ServerLaunchConfigurator>.Instance);
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    private sealed class EmptyAssetProvider : IAssetProvider
    {
        public Stream? GetAssetStream(string assetName) => null;
    }
}

internal sealed record PlayitAgentHarness(
    PlayitAgentService Service,
    PlayitAgentStateMachine StateMachine);

internal sealed class RecordingNotificationService : INotificationService
{
    public List<(string Title, string Message)> Messages { get; } = new();

    public void ShowInformation(string title, string message)
    {
        Messages.Add((title, message));
    }
}

internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly Func<string, HttpClient> _factory;

    public TestHttpClientFactory(Func<string, HttpClient> factory)
    {
        _factory = factory;
    }

    public HttpClient CreateClient(string name)
    {
        return _factory(name);
    }
}

internal sealed class DelegateHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

    public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request, cancellationToken));
    }
}
