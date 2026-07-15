using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.WhatsNew;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Infrastructure.Backups;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Infrastructure.WhatsNew;

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
        private readonly PocketMC.Infrastructure.Diagnostics.DependencyHealthMonitor _healthMonitor;
        private readonly InstanceRegistry _registry;
        private readonly IDiscordRpcService _discordRpcService;
        private readonly ITelemetryService _telemetryService;
        private readonly WhatsNewService _whatsNewService;
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
            PocketMC.Infrastructure.Diagnostics.DependencyHealthMonitor healthMonitor,
            InstanceRegistry registry,
            IDiscordRpcService discordRpcService,
            ITelemetryService telemetryService,
            WhatsNewService whatsNewService,
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
            _whatsNewService = whatsNewService;
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
                
                _settingsManager.SettingsSaved -= OnSettingsSaved;
                _settingsManager.SettingsSaved += OnSettingsSaved;

                if (string.IsNullOrWhiteSpace(settings.AppRootPath) || 
                    !Directory.Exists(settings.AppRootPath) || 
                    IsTemporaryPath(settings.AppRootPath))
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

        private void OnSettingsSaved(object? sender, AppSettings updatedSettings)
        {
            _applicationState.ApplySettings(updatedSettings);
        }

        private bool IsTemporaryPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                string tempDir = Path.GetTempPath();
                if (string.IsNullOrEmpty(tempDir))
                {
                    return false;
                }

                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullTempDir = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(fullPath, fullTempDir, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(fullTempDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(fullTempDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Fallback check: Check if the path contains standard Temp folders or test subfolders
                if (fullPath.IndexOf(@"\AppData\Local\Temp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fullPath.IndexOf(@"\Temp\PocketMC.Tests", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
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

            _settingsManager.SettingsSaved -= OnSettingsSaved;
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

            _host.NavigateToDashboard();

            if (!settings.HasCompletedFirstLaunch)
            {
                settings = _settingsManager.Load();
                settings.HasCompletedFirstLaunch = true;
                _settingsManager.Save(settings);
                _applicationState.ApplySettings(settings);
            }
            else
            {
                ShowWhatsNewIfNeeded();
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

        private void ShowWhatsNewIfNeeded()
        {
            try
            {
                if (!_whatsNewService.ShouldShow())
                {
                    return;
                }

                string currentVersion = _whatsNewService.GetCurrentVersion();
                ChangelogEntry? changelog = _whatsNewService.LoadChangelog();
                _host!.ShowWhatsNewDialog(changelog, currentVersion);
                _whatsNewService.MarkAsSeen();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to show What's New dialog. Continuing normal startup.");
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
            string? logPath = WriteStartupFailureLog(ex);
            string message = logPath == null
                ? "PocketMC could not initialize the main workflow. Check the debug log for details."
                : $"PocketMC could not initialize the main workflow. Details were written to:{Environment.NewLine}{logPath}";
            _host?.ShowError("Initialization Error", message);
            _host?.ShutdownApplication();
        }

        private static string? WriteStartupFailureLog(Exception exception)
        {
            try
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PocketMC",
                    "logs");

                Directory.CreateDirectory(logDirectory);

                string logPath = Path.Combine(
                    logDirectory,
                    $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

                File.WriteAllText(logPath, exception.ToString());
                return logPath;
            }
            catch
            {
                return null;
            }
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

