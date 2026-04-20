using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Tunnel
{
    /// <summary>
    /// Orchestrates the Playit.gg agent by coordinating process management, 
    /// state tracking, and log parsing.
    /// Implements NET-02, NET-03, NET-04, NET-05, NET-11.
    /// </summary>
    public sealed class PlayitAgentService : IDisposable
    {
        private static readonly Regex ClaimUrlRegex = new(
            @"(Visit link to setup |Approve program at )(?<url>https://playit\.gg/claim/[A-Za-z0-9\-]+)",
            RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        private static readonly Regex TunnelRunningRegex = new(
            @"tunnel running",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        private readonly PlayitAgentProcessManager _processManager;
        private readonly PlayitAgentStateMachine _stateMachine;
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly WindowsToastNotificationService _toastNotificationService;
        private readonly DownloaderService _downloaderService;
        private readonly ILogger<PlayitAgentService> _logger;

        private bool _claimUrlAlreadyFired;
        private bool _tunnelRunningAlreadyFired;
        private bool _manualStopRequested;
        private int _unexpectedRestartAttempts;
        private CancellationTokenSource? _restartDelayCancellation;
        private CancellationTokenSource? _downloadCancellation;
        private volatile bool _isDownloadingBinary;

        private const int MaxUnexpectedRestartAttempts = 5;
        private const int BaseUnexpectedRestartDelaySeconds = 2;

        public PlayitAgentState State => _stateMachine.State;
        public bool IsDownloadingBinary => _isDownloadingBinary;
        public bool IsBinaryAvailable => _applicationState.IsConfigured && File.Exists(_applicationState.GetPlayitExecutablePath());
        public bool CanResumeDownload => _downloaderService.CanResumePlayitDownload();
        public bool IsRunning => _processManager.IsRunning;

        public event EventHandler<string>? OnClaimUrlReceived;
        public event EventHandler? OnTunnelRunning;
        public event EventHandler<PlayitAgentState>? OnStateChanged;
        public event EventHandler<int>? OnAgentExited;
        public event EventHandler<DownloadProgress>? OnDownloadProgressChanged;
        public event EventHandler<bool>? OnDownloadStatusChanged;

        public PlayitAgentService(
            ApplicationState applicationState,
            SettingsManager settingsManager,
            PlayitAgentProcessManager processManager,
            PlayitAgentStateMachine stateMachine,
            WindowsToastNotificationService toastNotificationService,
            DownloaderService downloaderService,
            ILogger<PlayitAgentService> logger)
        {
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _processManager = processManager;
            _stateMachine = stateMachine;
            _toastNotificationService = toastNotificationService;
            _downloaderService = downloaderService;
            _logger = logger;

            _processManager.OnOutputLineReceived += OnProcessOutput;
            _processManager.OnErrorLineReceived += OnProcessError;
            _processManager.OnProcessExited += OnProcessExitedCore;
            _stateMachine.OnStateChanged += s => OnStateChanged?.Invoke(this, s);
        }

        public void Start()
        {
            CancelPendingRestart();
            if (IsRunning) return;

            if (!_applicationState.IsConfigured)
            {
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                return;
            }

            string playitPath = _applicationState.GetPlayitExecutablePath();
            if (!File.Exists(playitPath))
            {
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                _processManager.Log("ERROR: playit.exe not found at " + playitPath);
                return;
            }

            _claimUrlAlreadyFired = false;
            _tunnelRunningAlreadyFired = false;
            _manualStopRequested = false;
            _stateMachine.TransitionTo(PlayitAgentState.Starting);

            string logPath = Path.Combine(_applicationState.GetRequiredAppRootPath(), "tunnel", "playit-agent.log");
            _processManager.Start(playitPath, logPath);
            _processManager.Log($"INFO: playit.exe started (PID: {_processManager.ProcessId})");
        }

        public void Stop()
        {
            _manualStopRequested = true;
            CancelPendingRestart();
            _processManager.Stop();
            _stateMachine.TransitionTo(PlayitAgentState.Stopped);
            Interlocked.Exchange(ref _unexpectedRestartAttempts, 0);
        }

        public async Task RestartAsync(int delayMs = 500, CancellationToken token = default)
        {
            Stop();
            if (delayMs > 0) await Task.Delay(delayMs, token);
            token.ThrowIfCancellationRequested();
            _manualStopRequested = false;
            Start();
        }

        private void OnProcessOutput(string line)
        {
            string safeLine = LogSanitizer.SanitizePlayitLine(line);
            _processManager.Log("STDOUT: " + safeLine);

            if (line.Contains("Invalid secret, do you want to reset", StringComparison.OrdinalIgnoreCase))
            {
                RecoverFromInvalidSecret();
                return;
            }

            var claimMatch = ClaimUrlRegex.Match(line);
            if (claimMatch.Success && !_claimUrlAlreadyFired)
            {
                _claimUrlAlreadyFired = true;
                _stateMachine.TransitionTo(PlayitAgentState.WaitingForClaim);
                OnClaimUrlReceived?.Invoke(this, claimMatch.Groups["url"].Value);
            }

            if (TunnelRunningRegex.IsMatch(line) && !_tunnelRunningAlreadyFired)
            {
                _tunnelRunningAlreadyFired = true;
                _stateMachine.TransitionTo(PlayitAgentState.Connected);
                _toastNotificationService.ShowAgentConnected();
                OnTunnelRunning?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnProcessError(string line) => _processManager.Log("STDERR: " + LogSanitizer.SanitizePlayitLine(line));

        private void OnProcessExitedCore(int exitCode)
        {
            _processManager.Log($"INFO: playit.exe exited with code {exitCode}");
            if (_manualStopRequested)
            {
                _stateMachine.TransitionTo(PlayitAgentState.Stopped);
                return;
            }

            if (State == PlayitAgentState.Connected || State == PlayitAgentState.Starting || State == PlayitAgentState.WaitingForClaim)
            {
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                OnAgentExited?.Invoke(this, exitCode);
                _ = ScheduleRestartAsync(exitCode);
            }
            else
            {
                _stateMachine.TransitionTo(PlayitAgentState.Stopped);
            }
        }

        private async Task ScheduleRestartAsync(int exitCode)
        {
            int attempt = Interlocked.Increment(ref _unexpectedRestartAttempts);
            if (attempt > MaxUnexpectedRestartAttempts)
            {
                _processManager.Log("ERROR: playit.exe hit the max restart limit.");
                return;
            }

            int delaySeconds = ServerProcessManager.CalculateRestartDelaySeconds(BaseUnexpectedRestartDelaySeconds, attempt - 1);
            _processManager.Log($"WARN: Retrying in {delaySeconds}s (attempt {attempt}/{MaxUnexpectedRestartAttempts}).");

            _restartDelayCancellation = new CancellationTokenSource();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _restartDelayCancellation.Token);
                if (!_manualStopRequested) Start();
            }
            catch (TaskCanceledException) { }
            finally { _restartDelayCancellation?.Dispose(); _restartDelayCancellation = null; }
        }

        private void RecoverFromInvalidSecret()
        {
            _processManager.Log("INFO: Invalid secret detected. Deleting config and restarting...");
            try
            {
                string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
                if (File.Exists(tomlPath)) File.Delete(tomlPath);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete playit config."); }

            _ = Task.Run(async () => { Stop(); await Task.Delay(500); _manualStopRequested = false; Start(); });
        }

        private void CancelPendingRestart()
        {
            _restartDelayCancellation?.Cancel();
            _restartDelayCancellation?.Dispose();
            _restartDelayCancellation = null;
        }

        public async Task DownloadAgentAsync()
        {
            if (IsBinaryAvailable || _isDownloadingBinary) return;
            _downloadCancellation?.Cancel();
            _downloadCancellation = new CancellationTokenSource();
            _isDownloadingBinary = true;
            OnDownloadStatusChanged?.Invoke(this, true);
            try
            {
                var progress = new Progress<DownloadProgress>(p => OnDownloadProgressChanged?.Invoke(this, p));
                await _downloaderService.EnsurePlayitDownloadedAsync(_applicationState.GetRequiredAppRootPath(), progress, _downloadCancellation.Token);
            }
            finally { _isDownloadingBinary = false; OnDownloadStatusChanged?.Invoke(this, false); }
        }

        public void Dispose()
        {
            _processManager.OnOutputLineReceived -= OnProcessOutput;
            _processManager.OnErrorLineReceived -= OnProcessError;
            _processManager.OnProcessExited -= OnProcessExitedCore;
            _processManager.Dispose();
            _downloadCancellation?.Cancel();
            _downloadCancellation?.Dispose();
        }
    }
}
