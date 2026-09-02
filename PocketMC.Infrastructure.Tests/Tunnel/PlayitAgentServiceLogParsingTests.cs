using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Infrastructure.Tunnel;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Tunnel;

public class PlayitAgentServiceLogParsingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempSettingsFile;
    private readonly ApplicationState _appState;
    private readonly SettingsManager _settingsManager;
    private readonly PlayitAgentProcessManager _processManager;
    private readonly PlayitAgentStateMachine _stateMachine;
    private readonly PlayitPartnerProvisioningClient _provisioningClient;
    private readonly WindowsToastNotificationService _toastService;
    private readonly DownloaderService _downloaderService;

    public PlayitAgentServiceLogParsingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pocketmc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempSettingsFile = Path.Combine(_tempDir, "settings.json");

        _appState = new ApplicationState();
        _appState.Settings.AppRootPath = _tempDir;
        _appState.Settings.PlayitConfigDirectory = Path.Combine(_tempDir, "playit_gg");

        _settingsManager = new SettingsManager(_tempSettingsFile, NullLogger<SettingsManager>.Instance);
        _processManager = new PlayitAgentProcessManager(new JobObject(), NullLogger<PlayitAgentProcessManager>.Instance);
        _stateMachine = new PlayitAgentStateMachine();
        _provisioningClient = new PlayitPartnerProvisioningClient(_appState, _settingsManager, NullLogger<PlayitPartnerProvisioningClient>.Instance);
        _toastService = new WindowsToastNotificationService(NullLogger<WindowsToastNotificationService>.Instance);
        _downloaderService = new DownloaderService(new TestHttpClientFactory(), NullLogger<DownloaderService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void OnProcessOutput_WhenV1_0_10LogReceived_ExtractsAgentIdAndVersionAndConnects()
    {
        using var service = new PlayitAgentService(
            _appState,
            _settingsManager,
            _processManager,
            _stateMachine,
            _provisioningClient,
            _toastService,
            _downloaderService,
            NullLogger<PlayitAgentService>.Instance);

        bool tunnelRunningFired = false;
        service.OnTunnelRunning += (s, e) => tunnelRunningFired = true;

        MethodInfo? onProcessOutputMethod = typeof(PlayitAgentService).GetMethod("OnProcessOutput", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onProcessOutputMethod);

        // Simulate startup log
        string startupLog = "2026-09-02T08:08:08.865016Z  INFO playitd::daemon: Starting playitd socket_path=None secret_path=Some(\"playit.toml\") version=1.0.10";
        onProcessOutputMethod.Invoke(service, new object[] { startupLog });

        // Simulate connection log
        string connectedLog = "2026-09-02T08:08:11.276680Z  INFO playitd::daemon: playit connected; tunnels loaded agent_id=9d53fba3-3551-47e5-a00e-ed188be26bda tunnel_count=2 pending_tunnel_count=0 disabled_tunnel_count=0 account_status=\"verified\"";
        onProcessOutputMethod.Invoke(service, new object[] { connectedLog });

        Assert.True(tunnelRunningFired);
        Assert.Equal(PlayitAgentState.Connected, service.State);
        Assert.Equal("9d53fba3-3551-47e5-a00e-ed188be26bda", _appState.Settings.PlayitPartnerConnection?.AgentId);
        Assert.Equal("1.0.10", _appState.Settings.PlayitVersion);
        Assert.Equal("1.0.10", _appState.Settings.PlayitPartnerConnection?.AgentVersion);
    }

    [Fact]
    public void OnProcessOutput_WhenLegacyLogReceived_TransitionsToConnected()
    {
        using var service = new PlayitAgentService(
            _appState,
            _settingsManager,
            _processManager,
            _stateMachine,
            _provisioningClient,
            _toastService,
            _downloaderService,
            NullLogger<PlayitAgentService>.Instance);

        bool tunnelRunningFired = false;
        service.OnTunnelRunning += (s, e) => tunnelRunningFired = true;

        MethodInfo? onProcessOutputMethod = typeof(PlayitAgentService).GetMethod("OnProcessOutput", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onProcessOutputMethod);

        string legacyLog = "2026-09-02 08:00:00 [INFO] tunnel running";
        onProcessOutputMethod.Invoke(service, new object[] { legacyLog });

        Assert.True(tunnelRunningFired);
        Assert.Equal(PlayitAgentState.Connected, service.State);
    }

    [Fact]
    public void OnProcessError_WhenV1_0_10LogReceivedOnStderr_ExtractsAgentIdAndConnects()
    {
        using var service = new PlayitAgentService(
            _appState,
            _settingsManager,
            _processManager,
            _stateMachine,
            _provisioningClient,
            _toastService,
            _downloaderService,
            NullLogger<PlayitAgentService>.Instance);

        bool tunnelRunningFired = false;
        service.OnTunnelRunning += (s, e) => tunnelRunningFired = true;

        MethodInfo? onProcessErrorMethod = typeof(PlayitAgentService).GetMethod("OnProcessError", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onProcessErrorMethod);

        // Rust tracing prints to STDERR by default in v1.0.10
        string connectedStderrLog = "2026-09-02T08:26:30.544904Z  INFO playitd::daemon: playit connected; tunnels loaded agent_id=9d53fba3-3551-47e5-a00e-ed188be26bda tunnel_count=2 pending_tunnel_count=0 disabled_tunnel_count=0 account_status=\"verified\"";
        onProcessErrorMethod.Invoke(service, new object[] { connectedStderrLog });

        Assert.True(tunnelRunningFired);
        Assert.Equal(PlayitAgentState.Connected, service.State);
        Assert.Equal("9d53fba3-3551-47e5-a00e-ed188be26bda", _appState.Settings.PlayitPartnerConnection?.AgentId);
    }

    [Theory]
    [InlineData("2026-09-02T08:51:13.853805Z ERROR playitd::daemon: Failed to load agent data error=ApiError(Auth(InvalidAgentKey))")]
    [InlineData("2026-09-02T08:51:46.492620Z  WARN playitd::daemon: configured agent secret is no longer valid error=ApiError(Auth(InvalidAgentKey))")]
    [InlineData("2026-09-02T08:51:46.492739Z  INFO playitd::daemon: Waiting for frontend secret provisioning over IPC secret_path=C:\\path\\playit.toml")]
    [InlineData("2026-09-02T08:51:42.701200Z ERROR playit_agent_core::playit_agent: failed to reload_control_addr error=ApiError(Auth(InvalidAgentKey))")]
    [InlineData("2026-09-02T08:26:30.233086Z ERROR playit_agent_core: Secret error: invalid secret")]
    [InlineData("2026-09-02T08:26:30.233086Z WARN playit: reason=\"agent_not_found\"")]
    public void OnProcessError_WhenDeletedOrInvalidSecretLogReceived_WipesCredentialsAndTransitionsToAwaitingSetup(string errorLog)
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection
        {
            AgentId = "dead-agent-id",
            AgentSecretKey = "dead-secret-key"
        };

        using var service = new PlayitAgentService(
            _appState,
            _settingsManager,
            _processManager,
            _stateMachine,
            _provisioningClient,
            _toastService,
            _downloaderService,
            NullLogger<PlayitAgentService>.Instance);

        MethodInfo? onProcessErrorMethod = typeof(PlayitAgentService).GetMethod("OnProcessError", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onProcessErrorMethod);

        onProcessErrorMethod.Invoke(service, new object[] { errorLog });

        Assert.Equal(PlayitAgentState.AwaitingSetupCode, service.State);
        Assert.Null(_appState.Settings.PlayitPartnerConnection);
    }

    [Fact]
    public void OnProcessError_WhenSessionNotSetupDuringStartup_DoesNotWipeCredentials()
    {
        _appState.Settings.PlayitPartnerConnection = new PlayitPartnerConnection
        {
            AgentId = "valid-agent-id",
            AgentSecretKey = "valid-secret-key"
        };

        using var service = new PlayitAgentService(
            _appState,
            _settingsManager,
            _processManager,
            _stateMachine,
            _provisioningClient,
            _toastService,
            _downloaderService,
            NullLogger<PlayitAgentService>.Instance);

        MethodInfo? onProcessErrorMethod = typeof(PlayitAgentService).GetMethod("OnProcessError", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onProcessErrorMethod);

        string normalStartupLog = "2026-09-02T08:31:16.743615Z  WARN playit_agent_core::agent_control::maintained_control: control session expired; reconnecting reason=SessionNotSetup";
        onProcessErrorMethod.Invoke(service, new object[] { normalStartupLog });

        // Must NOT wipe credentials on normal startup handshake
        Assert.NotNull(_appState.Settings.PlayitPartnerConnection);
    }

    private sealed class TestHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new();
    }
}
