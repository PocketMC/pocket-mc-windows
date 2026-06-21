using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Features.Shell
{
    public sealed class ShellStartupCoordinator : IDisposable
    {
        private readonly SettingsManager _settingsManager;
        private readonly ApplicationState _applicationState;
        private readonly BackupSchedulerService _backupScheduler;
        private readonly IServerLifecycleService _serverLifecycleService;
        private readonly JavaProvisioningService _javaProvisioningService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly IResourceMonitorService _resourceMonitorService;
        private readonly PocketMC.Desktop.Features.Diagnostics.DependencyHealthMonitor _healthMonitor;
        private readonly InstanceRegistry _registry;
        private readonly IDiscordRpcService _discordRpcService;
        private readonly ITelemetryService _telemetryService;
        private readonly AppStartupOptions _startupOptions;
        private readonly ILogger<ShellStartupCoordinator> _logger;
        private IStartupShellHost? _host;
        private bool _startupServicesStarted;
        private bool _playitStartupAttempted;
        private bool _isDisposed;

        public ShellStartupCoordinator(
            SettingsManager settingsManager,
            ApplicationState applicationState,
            BackupSchedulerService backupScheduler,
            IServerLifecycleService serverLifecycleService,
            JavaProvisioningService javaProvisioningService,
            PlayitAgentService playitAgentService,
            IResourceMonitorService resourceMonitorService,
            PocketMC.Desktop.Features.Diagnostics.DependencyHealthMonitor healthMonitor,
            InstanceRegistry registry,
            IDiscordRpcService discordRpcService,
            ITelemetryService telemetryService,
            AppStartupOptions startupOptions,
            ILogger<ShellStartupCoordinator> logger)
        {
            _settingsManager = settingsManager;
            _applicationState = applicationState;
            _backupScheduler = backupScheduler;
            _serverLifecycleService = serverLifecycleService;
            _javaProvisioningService = javaProvisioningService;
            _playitAgentService = playitAgentService;
            _resourceMonitorService = resourceMonitorService;
            _healthMonitor = healthMonitor;
            _registry = registry;
            _discordRpcService = discordRpcService;
            _telemetryService = telemetryService;
            _startupOptions = startupOptions;
            _logger = logger;
        }

        public void AttachHost(IStartupShellHost host)
        {
            _host = host;
            _playitAgentService.OnTunnelRunning += OnPlayitTunnelRunning;
        }

        public void Start()
        {
            ThrowIfNoHost();

            try
            {
                AppSettings settings = _settingsManager.Load();
                if (string.IsNullOrWhiteSpace(settings.AppRootPath))
                {
                    _host!.ShowRootDirectorySetup();
                    return;
                }

                ContinueStartupFlow(settings);
            }
            catch (Exception ex)
            {
                HandleStartupFailure(ex);
            }
        }

        public void CompleteRootDirectorySelection(string rootPath)
        {
            ThrowIfNoHost();

            try
            {
                var settings = _settingsManager.Load();
                settings.AppRootPath = rootPath;

                Directory.CreateDirectory(rootPath);
                _settingsManager.Save(settings);
                ContinueStartupFlow(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist the PocketMC root directory selection.");
                _host!.ShowError("Root Folder Error", $"PocketMC could not save the selected root folder.\n\n{ex.Message}");
            }
        }

        public void Shutdown()
        {
            if (_isDisposed)
            {
                return;
            }

            _playitAgentService.OnTunnelRunning -= OnPlayitTunnelRunning;
            _backupScheduler.Stop();
            _healthMonitor.StopMonitoring();
            _discordRpcService.Shutdown();
            _telemetryService.Shutdown();
            // Go through the lifecycle layer so shutdown also releases port leases and
            // clears cached tunnel state instead of only killing the OS processes.
            _serverLifecycleService.KillAll();
            _host = null;
            _isDisposed = true;
        }

        public void Dispose()
        {
            Shutdown();
        }

        private void ContinueStartupFlow(AppSettings settings)
        {
            _host!.CompleteRootDirectorySetup();
            _applicationState.ApplySettings(settings);
            _host.ApplyTheme();
            _host.RequestMicaUpdate();

            if (!_startupServicesStarted)
            {
                _backupScheduler.Start();
                _healthMonitor.StartMonitoring();
                _javaProvisioningService.StartBackgroundProvisioning();

                if (!settings.HasCompletedFirstLaunch)
                {
                    _ = _playitAgentService.DownloadAgentAsync();
                }

                _discordRpcService.Initialize();
                _telemetryService.Initialize();
                _startupServicesStarted = true;
            }

            if (!settings.HasCompletedFirstLaunch)
            {
                _host.NavigateToPlayitSetup();
            }
            else
            {
                _host.NavigateToDashboard();
                TriggerServerAutoStarts();
            }

            if (!_playitStartupAttempted)
            {
                _playitStartupAttempted = true;
                TryStartPlayitAgentOnLaunch();
            }
        }

        private void TryStartPlayitAgentOnLaunch()
        {
            try
            {
                if (!File.Exists(_applicationState.GetPlayitExecutablePath()))
                {
                    _logger.LogInformation("Playit agent binary is missing; startup auto-connect was skipped.");
                    return;
                }

                _playitAgentService.Start();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Playit auto-connect failed during app startup. The user can retry from the Tunnel page.");
            }
        }

        private void OnPlayitTunnelRunning(object? sender, EventArgs e)
        {
            if (_host == null)
            {
                return;
            }

            AppSettings settings = _settingsManager.Load();
            if (settings.HasCompletedFirstLaunch)
            {
                return;
            }

            settings.HasCompletedFirstLaunch = true;
            _settingsManager.Save(settings);
            _applicationState.ApplySettings(settings);
            _host.NavigateToDashboard();
        }

        private void HandleStartupFailure(Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the PocketMC startup flow.");
            _host?.ShowError("Initialization Error", "PocketMC could not initialize the main workflow. Check the debug log for details.");
            _host?.ShutdownApplication();
        }

        private void ThrowIfNoHost()
        {
            if (_host == null)
            {
                throw new InvalidOperationException("Shell startup host has not been attached.");
            }
        }

        private async void TriggerServerAutoStarts()
        {
            try
            {
                _logger.LogInformation("Processing auto-start servers on app startup...");
                var instances = _registry.GetAll();
                foreach (var meta in instances)
                {
                    if (meta.AutoStartWithApp)
                    {
                        _logger.LogInformation("Auto-starting server instance: {ServerName} ({InstanceId})", meta.Name, meta.Id);
                        _ = StartServerInstanceAsync(meta);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during server auto-start sequence.");
            }
        }

        private async Task StartServerInstanceAsync(InstanceMetadata meta)
        {
            try
            {
                await _serverLifecycleService.StartAsync(meta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-start server instance {ServerName} ({InstanceId}).", meta.Name, meta.Id);
            }
        }
    }
}
