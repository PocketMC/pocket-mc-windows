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
        private readonly PlayitPartnerProvisioningClient _partnerProvisioningClient;
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
        public bool IsRunning => _processManager.IsRunning;
        public string? LastErrorMessage { get; private set; }
        public PlayitPartnerConnection? PartnerConnection => _applicationState.Settings.PlayitPartnerConnection;

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
            PlayitPartnerProvisioningClient partnerProvisioningClient,
            WindowsToastNotificationService toastNotificationService,
            DownloaderService downloaderService,
            ILogger<PlayitAgentService> logger)
        {
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _processManager = processManager;
            _stateMachine = stateMachine;
            _partnerProvisioningClient = partnerProvisioningClient;
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
            LastErrorMessage = null;

            if (!_applicationState.IsConfigured)
            {
                LastErrorMessage = "PocketMC is not configured yet.";
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                return;
            }

            string playitPath = _applicationState.GetPlayitExecutablePath();
            if (!File.Exists(playitPath))
            {
                LastErrorMessage = "playit.exe is missing.";
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                _processManager.Log("ERROR: playit.exe not found at " + playitPath);
                return;
            }

            TryImportLegacyTomlConnection();
            string? secretKey = _applicationState.Settings.PlayitPartnerConnection?.AgentSecretKey;
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                if (State != PlayitAgentState.ReauthRequired)
                {
                    _stateMachine.TransitionTo(PlayitAgentState.AwaitingSetupCode);
                }

                return;
            }

            EnsureRuntimeToml(secretKey);
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

        public async Task<PlayitPartnerCreateAgentResult> ConnectWithSetupCodeAsync(string setupCode, CancellationToken token = default)
        {
            LastErrorMessage = null;

            if (!_applicationState.IsConfigured)
            {
                LastErrorMessage = "PocketMC is not configured yet.";
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                return new PlayitPartnerCreateAgentResult { Success = false, ErrorMessage = LastErrorMessage };
            }

            string playitPath = _applicationState.GetPlayitExecutablePath();
            if (!File.Exists(playitPath))
            {
                LastErrorMessage = "Download the Playit agent before connecting.";
                _stateMachine.TransitionTo(PlayitAgentState.Error);
                return new PlayitPartnerCreateAgentResult { Success = false, ErrorMessage = LastErrorMessage };
            }

            _stateMachine.TransitionTo(PlayitAgentState.ProvisioningAgent);
            PlayitPartnerAgentVersion agentVersion = PlayitEmbeddedAgentVersionResolver.Resolve(playitPath);
            PlayitPartnerCreateAgentResult result = await _partnerProvisioningClient.CreateAgentAsync(
                new PlayitPartnerCreateAgentRequest
                {
                    SetupCode = setupCode.Trim(),
                    AgentVersion = agentVersion
                },
                token);

            if (!result.Success || result.Response == null)
            {
                LastErrorMessage = result.ErrorMessage;
                _stateMachine.TransitionTo(PlayitAgentState.AwaitingSetupCode);
                return result;
            }

            SavePartnerConnection(
                result.Response.AgentId,
                result.Response.AgentSecretKey,
                result.Response.AccountId,
                result.Response.ConnectedEmail,
                agentVersion.ToString());

            Stop();
            _manualStopRequested = false;
            Start();
            return result;
        }

        public void Disconnect()
        {
            Stop();
            ClearPartnerConnection();
            DeleteRuntimeToml();
            LastErrorMessage = null;
            _stateMachine.TransitionTo(PlayitAgentState.Disconnected);
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
                LastErrorMessage = "The Playit agent requested a manual claim flow. Reconnect PocketMC with a fresh setup code.";
                _stateMachine.TransitionTo(PlayitAgentState.ReauthRequired);
            }

            if (TunnelRunningRegex.IsMatch(line) && !_tunnelRunningAlreadyFired)
            {
                _tunnelRunningAlreadyFired = true;
                LastErrorMessage = null;
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

            if (State == PlayitAgentState.Connected ||
                State == PlayitAgentState.Starting ||
                State == PlayitAgentState.ProvisioningAgent)
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
            _processManager.Log("INFO: Invalid secret detected. Clearing saved Playit credentials.");
            ClearPartnerConnection();
            DeleteRuntimeToml();
            LastErrorMessage = "The saved Playit credentials are no longer valid. Reconnect with a new setup code.";
            _manualStopRequested = true;
            _processManager.Stop();
            _stateMachine.TransitionTo(PlayitAgentState.ReauthRequired);
        }

        private void CancelPendingRestart()
        {
            _restartDelayCancellation?.Cancel();
            _restartDelayCancellation?.Dispose();
            _restartDelayCancellation = null;
        }

        private void SavePartnerConnection(string agentId, string agentSecretKey, long? accountId, string? connectedEmail, string agentVersion)
        {
            var settings = _settingsManager.Load();
            settings.PlayitPartnerConnection = new PlayitPartnerConnection
            {
                AgentId = agentId,
                AgentSecretKey = agentSecretKey,
                AccountId = accountId,
                ConnectedEmail = connectedEmail,
                Platform = "windows",
                AgentVersion = agentVersion,
                ConnectedAtUtc = DateTimeOffset.UtcNow
            };

            _settingsManager.Save(settings);
            _applicationState.ApplySettings(settings);
        }

        private void ClearPartnerConnection()
        {
            var settings = _settingsManager.Load();
            settings.PlayitPartnerConnection = null;
            _settingsManager.Save(settings);
            _applicationState.ApplySettings(settings);
        }

        private void TryImportLegacyTomlConnection()
        {
            if (!string.IsNullOrWhiteSpace(_applicationState.Settings.PlayitPartnerConnection?.AgentSecretKey))
            {
                return;
            }

            string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            if (!File.Exists(tomlPath))
            {
                return;
            }

            string? secretKey = null;
            try
            {
                string content = File.ReadAllText(tomlPath);
                Match match = Regex.Match(content, @"secret_key\s*=\s*""([^""]+)""");
                secretKey = match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import a legacy Playit TOML secret.");
            }

            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return;
            }

            SavePartnerConnection(
                _applicationState.Settings.PlayitPartnerConnection?.AgentId ?? string.Empty,
                secretKey,
                _applicationState.Settings.PlayitPartnerConnection?.AccountId,
                _applicationState.Settings.PlayitPartnerConnection?.ConnectedEmail,
                PlayitEmbeddedAgentVersionResolver.Resolve(_applicationState.GetPlayitExecutablePath()).ToString());
        }

        private void EnsureRuntimeToml(string secretKey)
        {
            string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            string? directory = Path.GetDirectoryName(tomlPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tomlPath, $"secret_key = \"{secretKey}\"{Environment.NewLine}");
        }

        private void DeleteRuntimeToml()
        {
            try
            {
                string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
                if (File.Exists(tomlPath))
                {
                    File.Delete(tomlPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete playit config.");
            }
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
