using PocketMC.Infrastructure.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Security;
using PocketMC.Infrastructure.Backups;


using PocketMC.Infrastructure.Networking;
using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;

using PocketMC.Infrastructure;
using Microsoft.Win32;
using System.Net.NetworkInformation;

namespace PocketMC.Infrastructure.Tunnel
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
            @"(tunnel running|playit connected; tunnels loaded|playit connected)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        private static readonly Regex AgentIdRegex = new(
            @"agent_id=(?<agentId>[a-f0-9\-]{8,})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        private static readonly Regex VersionRegex = new(
            @"version=(?<version>[0-9\.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        private static readonly Regex LegacyTomlSecretRegex = new(
            @"secret_key\s*=\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private readonly PlayitAgentProcessManager _processManager;
        private readonly PlayitAgentStateMachine _stateMachine;
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly PlayitPartnerProvisioningClient _partnerProvisioningClient;
        private readonly WindowsToastNotificationService _toastNotificationService;
        private readonly DownloaderService _downloaderService;
        private readonly ILogger<PlayitAgentService> _logger;
        private readonly PlayitApiClient? _playitApiClient;

        private bool _claimUrlAlreadyFired;
        private bool _tunnelRunningAlreadyFired;
        private bool _manualStopRequested;
        private int _unexpectedRestartAttempts;
        private CancellationTokenSource? _restartDelayCancellation;
        private CancellationTokenSource? _downloadCancellation;
        private CancellationTokenSource? _networkChangeCancellation;
        private readonly object _networkEventLock = new();
        private volatile bool _isDownloadingBinary;

        private const int MaxUnexpectedRestartAttempts = 5;
        private const int BaseUnexpectedRestartDelaySeconds = 2;

        public PlayitAgentState State => _stateMachine.State;
        public bool IsDownloadingBinary => _isDownloadingBinary;
        public bool IsBinaryAvailable => _applicationState.IsConfigured && File.Exists(_applicationState.GetPlayitExecutablePath());
        public bool IsRunning => _processManager.IsRunning;
        public string? LastErrorMessage { get; private set; }
        public PlayitPartnerConnection? PartnerConnection => _applicationState.Settings.PlayitPartnerConnection;

        public event EventHandler? OnTunnelRunning;
        public event EventHandler<PlayitAgentState>? OnStateChanged;
        public event EventHandler<int>? OnAgentExited;
        public event EventHandler<DownloadProgress>? OnDownloadProgressChanged;
        public event EventHandler<bool>? OnDownloadStatusChanged;
        public event Action<string>? OnLogReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _recentLogs = new();
        private const int MaxRecentLogs = 2000;

        public PlayitAgentService(
            ApplicationState applicationState,
            SettingsManager settingsManager,
            PlayitAgentProcessManager processManager,
            PlayitAgentStateMachine stateMachine,
            PlayitPartnerProvisioningClient partnerProvisioningClient,
            WindowsToastNotificationService toastNotificationService,
            DownloaderService downloaderService,
            ILogger<PlayitAgentService> logger,
            PlayitApiClient? playitApiClient = null)
        {
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _processManager = processManager;
            _stateMachine = stateMachine;
            _partnerProvisioningClient = partnerProvisioningClient;
            _toastNotificationService = toastNotificationService;
            _downloaderService = downloaderService;
            _logger = logger;
            _playitApiClient = playitApiClient;

            _processManager.OnOutputLineReceived += OnProcessOutput;
            _processManager.OnErrorLineReceived += OnProcessError;
            _processManager.OnProcessExited += OnProcessExitedCore;
            _stateMachine.OnStateChanged += s => OnStateChanged?.Invoke(this, s);

            SubscribeSystemEvents();
        }

        public void Start()
        {
            CleanPendingDeletes();
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

            string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            EnsureRuntimeToml(secretKey);
            _claimUrlAlreadyFired = false;
            _tunnelRunningAlreadyFired = false;
            _manualStopRequested = false;
            _stateMachine.TransitionTo(PlayitAgentState.Starting);

            string logPath = Path.Combine(_applicationState.GetRequiredAppRootPath(), "tunnel", "playit-agent.log");
            string arguments = $"--secret-path \"{tomlPath}\"";
            _processManager.Start(playitPath, logPath, arguments);
            string startMsg = $"INFO: playit.exe started (PID: {_processManager.ProcessId})";
            _processManager.Log(startMsg);
            AppendLog($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {startMsg}");
        }

        public void Stop()
        {
            _manualStopRequested = true;
            CancelPendingRestart();
            _processManager.Stop();
            _stateMachine.TransitionTo(PlayitAgentState.Stopped);
            Interlocked.Exchange(ref _unexpectedRestartAttempts, 0);
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            _manualStopRequested = true;
            CancelPendingRestart();
            await _processManager.StopAsync(token);
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

        public IReadOnlyList<string> GetRecentLogs()
        {
            if (!_recentLogs.IsEmpty)
            {
                return _recentLogs.ToArray();
            }

            string logPath = GetLogFilePath();
            if (File.Exists(logPath))
            {
                try
                {
                    return File.ReadLines(logPath).TakeLast(500).ToList();
                }
                catch
                {
                    // Fallback if log file is locked
                }
            }

            return Array.Empty<string>();
        }

        public string GetLogFilePath()
        {
            if (!_applicationState.IsConfigured) return string.Empty;
            return Path.Combine(_applicationState.GetRequiredAppRootPath(), "tunnel", "playit-agent.log");
        }

        private void AppendLog(string line)
        {
            _recentLogs.Enqueue(line);
            while (_recentLogs.Count > MaxRecentLogs && _recentLogs.TryDequeue(out _)) { }
            OnLogReceived?.Invoke(line);
        }

        private void OnProcessOutput(string line)
        {
            string sanitized = LogSanitizer.SanitizePlayitLine(line);
            _processManager.Log("STDOUT: " + sanitized);
            AppendLog(sanitized);
            ProcessOutputLine(line);
        }

        private void OnProcessError(string line)
        {
            string sanitized = LogSanitizer.SanitizePlayitLine(line);
            _processManager.Log("STDERR: " + sanitized);
            AppendLog(sanitized);
            ProcessOutputLine(line);
        }

        private void ProcessOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (IsInvalidOrDeletedSecretLog(line))
            {
                RecoverFromInvalidSecret();
                return;
            }

            var claimMatch = ClaimUrlRegex.Match(line);
            if (claimMatch.Success && !_claimUrlAlreadyFired)
            {
                _claimUrlAlreadyFired = true;
                RecoverFromInvalidSecret("The Playit agent requested a new setup code. Click Setup Agent to link your account.");
                return;
            }

            var agentIdMatch = AgentIdRegex.Match(line);
            if (agentIdMatch.Success)
            {
                string detectedAgentId = agentIdMatch.Groups["agentId"].Value;
                if (!string.IsNullOrWhiteSpace(detectedAgentId))
                {
                    SyncAgentIdIfChanged(detectedAgentId);
                }
            }

            var versionMatch = VersionRegex.Match(line);
            if (versionMatch.Success)
            {
                string detectedVersion = versionMatch.Groups["version"].Value;
                if (!string.IsNullOrWhiteSpace(detectedVersion))
                {
                    SyncAgentVersionIfChanged(detectedVersion);
                }
            }

            if (TunnelRunningRegex.IsMatch(line) && !_tunnelRunningAlreadyFired)
            {
                _tunnelRunningAlreadyFired = true;
                LastErrorMessage = null;
                _stateMachine.TransitionTo(PlayitAgentState.Connected);
                if (_applicationState.Settings.EnableAgentConnectNotifications)
                {
                    _toastNotificationService.ShowAgentConnected();
                }
                OnTunnelRunning?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SyncAgentIdIfChanged(string agentId)
        {
            var partner = _applicationState.Settings.PlayitPartnerConnection;
            if (partner == null || string.IsNullOrWhiteSpace(partner.AgentId) || partner.AgentId != agentId)
            {
                var settings = _settingsManager.Load();
                settings.PlayitPartnerConnection ??= new PlayitPartnerConnection();
                settings.PlayitPartnerConnection.AgentId = agentId;
                if (string.IsNullOrWhiteSpace(settings.PlayitPartnerConnection.AgentVersion))
                {
                    settings.PlayitPartnerConnection.AgentVersion = "1.0.10";
                }
                _settingsManager.Save(settings);
                _applicationState.ApplySettings(settings);
                _logger.LogInformation("Updated Playit Agent ID from runtime log: {AgentId}", agentId);
            }
        }

        private void SyncAgentVersionIfChanged(string version)
        {
            var settings = _settingsManager.Load();
            bool changed = false;
            if (settings.PlayitVersion != version)
            {
                settings.PlayitVersion = version;
                changed = true;
            }
            if (settings.PlayitPartnerConnection != null && settings.PlayitPartnerConnection.AgentVersion != version)
            {
                settings.PlayitPartnerConnection.AgentVersion = version;
                changed = true;
            }
            if (changed)
            {
                _settingsManager.Save(settings);
                _applicationState.ApplySettings(settings);
                _logger.LogInformation("Updated Playit Agent version in settings: {Version}", version);
            }
        }

        private void OnProcessExitedCore(int exitCode)
        {
            string exitMsg = $"INFO: playit.exe exited with code {exitCode}";
            _processManager.Log(exitMsg);
            AppendLog($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {exitMsg}");
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

        private static bool IsInvalidOrDeletedSecretLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            return line.Contains("InvalidAgentKey", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("configured agent secret is no longer valid", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("Waiting for frontend secret provisioning", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("ApiError(Auth(", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("Invalid secret, do you want to reset", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("invalid secret", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("Secret error:", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("InvalidApiKey", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("NoLongerValid", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("AccountDoesNotExist", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("AccountNotAuthorized", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("AgentBlocked", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("AgentNotFound", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("reason=\"agent_not_found\"", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("reason=\"unauthorized\"", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("reason=Unauthorized", StringComparison.OrdinalIgnoreCase);
        }

        public void RecoverFromInvalidSecret(string? customMessage = null)
        {
            _processManager.Log("INFO: Invalid or deleted secret detected. Clearing saved Playit credentials.");
            ClearPartnerConnection();
            DeleteRuntimeToml();
            LastErrorMessage = customMessage ?? "The Playit agent was deleted or is no longer valid. Click Setup Agent to link a new agent.";
            _manualStopRequested = true;
            _processManager.Stop();
            _stateMachine.TransitionTo(PlayitAgentState.AwaitingSetupCode);
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
            string? existingSecretKey = _applicationState.Settings.PlayitPartnerConnection?.AgentSecretKey;
            string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            string? secretKey = existingSecretKey;

            if (string.IsNullOrWhiteSpace(secretKey) && File.Exists(tomlPath))
            {
                try
                {
                    string content = File.ReadAllText(tomlPath);
                    Match match = LegacyTomlSecretRegex.Match(content);
                    secretKey = match.Success ? match.Groups[1].Value : null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to import a legacy Playit TOML secret.");
                }
            }

            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return;
            }

            string agentId = _applicationState.Settings.PlayitPartnerConnection?.AgentId ?? string.Empty;
            string agentVersion = _applicationState.Settings.PlayitPartnerConnection?.AgentVersion ?? "1.0.10";

            if (string.IsNullOrWhiteSpace(agentId) && _playitApiClient != null)
            {
                try
                {
                    var rundataTask = _playitApiClient.GetAgentRundataAsync();
                    if (rundataTask.Wait(TimeSpan.FromSeconds(3)))
                    {
                        var rundata = rundataTask.Result;
                        if (rundata.Success && !string.IsNullOrWhiteSpace(rundata.AgentId))
                        {
                            agentId = rundata.AgentId;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve agent ID from rundata synchronously during import.");
                }
            }

            SavePartnerConnection(
                agentId,
                secretKey,
                _applicationState.Settings.PlayitPartnerConnection?.AccountId,
                _applicationState.Settings.PlayitPartnerConnection?.ConnectedEmail,
                agentVersion);
        }

        private void EnsureRuntimeToml(string secretKey)
        {
            string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            string? directory = Path.GetDirectoryName(tomlPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FileUtils.AtomicWriteAllText(tomlPath, $"secret_key = \"{secretKey}\"{Environment.NewLine}");
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

        public async Task<bool> DeleteAgentBinaryAsync(CancellationToken token = default)
        {
            await StopAsync(token);
            _downloadCancellation?.Cancel();

            string exePath = _applicationState.GetPlayitExecutablePath();
            await StopOrphanPlayitProcessesAsync(exePath, token);

            bool exeDeleted = false;
            bool partialDeleted = false;

            try
            {
                await DeleteFileWithRetryAsync(exePath, token);
                exeDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete playit.exe, attempting fallback to rename as delete-pending.");
                try
                {
                    string directory = Path.GetDirectoryName(exePath) ?? string.Empty;
                    string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    string pendingPath = Path.Combine(directory, $"playit.{timestamp}.delete-pending.exe");
                    if (File.Exists(exePath))
                    {
                        File.SetAttributes(exePath, FileAttributes.Normal);
                        File.Move(exePath, pendingPath);
                        _logger.LogInformation("Successfully renamed playit.exe to {PendingPath}", pendingPath);
                        exeDeleted = true;
                    }
                    else
                    {
                        exeDeleted = true;
                    }
                }
                catch (Exception renameEx)
                {
                    _logger.LogError(renameEx, "Failed to rename playit.exe to delete-pending.");
                }
            }

            string partialPath = exePath + ".partial";
            try
            {
                await DeleteFileWithRetryAsync(partialPath, token);
                partialDeleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete partial playit file, attempting fallback to rename.");
                try
                {
                    string directory = Path.GetDirectoryName(partialPath) ?? string.Empty;
                    string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    string pendingPath = Path.Combine(directory, $"playit.partial.{timestamp}.delete-pending.exe");
                    if (File.Exists(partialPath))
                    {
                        File.SetAttributes(partialPath, FileAttributes.Normal);
                        File.Move(partialPath, pendingPath);
                        partialDeleted = true;
                    }
                    else
                    {
                        partialDeleted = true;
                    }
                }
                catch (Exception renameEx)
                {
                    _logger.LogError(renameEx, "Failed to rename partial file to delete-pending.");
                }
            }

            // Fire state change event to let the UI refresh its state and buttons
            _stateMachine.TransitionTo(PlayitAgentState.Stopped);

            return exeDeleted && partialDeleted;
        }

        private async Task StopOrphanPlayitProcessesAsync(string exePath, CancellationToken token)
        {
            string targetPath = Path.GetFullPath(exePath);

            foreach (Process process in Process.GetProcessesByName("playit"))
            {
                try
                {
                    string? processPath = process.MainModule?.FileName;

                    if (!string.Equals(
                            Path.GetFullPath(processPath ?? string.Empty),
                            targetPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to inspect or terminate orphan playit process.");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static async Task DeleteFileWithRetryAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path))
                return;

            File.SetAttributes(path, FileAttributes.Normal);

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        // File is not locked, we can delete it safely
                    }
                    File.Delete(path);
                    return;
                }
                catch (IOException) when (attempt < 8)
                {
                    await Task.Delay(250 * attempt, token);
                }
                catch (UnauthorizedAccessException) when (attempt < 8)
                {
                    await Task.Delay(250 * attempt, token);
                }
            }

            // On the final attempt, verify exclusive access one last time.
            // If it throws, the exception will propagate to trigger the rename fallback.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
            }
            File.Delete(path);
        }

        public void CleanPendingDeletes()
        {
            if (!_applicationState.IsConfigured) return;

            try
            {
                string tunnelDir = Path.Combine(_applicationState.GetRequiredAppRootPath(), "tunnel");
                if (!Directory.Exists(tunnelDir)) return;

                foreach (string file in Directory.GetFiles(tunnelDir, "*.delete-pending.exe"))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        _logger.LogInformation("Cleaned up pending delete file: {File}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean up pending delete file on startup: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan for pending delete files.");
            }
        }

        private void SubscribeSystemEvents()
        {
            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not subscribe to SystemEvents.PowerModeChanged.");
            }

            try
            {
                NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not subscribe to NetworkChange.NetworkAddressChanged.");
            }
        }

        private void UnsubscribeSystemEvents()
        {
            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch { }

            try
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            }
            catch { }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                _logger.LogInformation("[PlayitAgentService] System resumed from sleep/standby. Scheduling agent socket re-sync.");
                _ = HandleSystemResumeOrNetworkChangeAsync("System Resume", delaySeconds: 2);
            }
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            lock (_networkEventLock)
            {
                _networkChangeCancellation?.Cancel();
                _networkChangeCancellation?.Dispose();
                _networkChangeCancellation = new CancellationTokenSource();
                CancellationToken token = _networkChangeCancellation.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1500, token);
                        if (token.IsCancellationRequested) return;
                        _logger.LogInformation("[PlayitAgentService] Network interface address change detected. Scheduling agent socket re-sync.");
                        await HandleSystemResumeOrNetworkChangeAsync("Network Change", delaySeconds: 1);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error handling network address changed event.");
                    }
                }, token);
            }
        }

        private async Task HandleSystemResumeOrNetworkChangeAsync(string reason, int delaySeconds)
        {
            if (_manualStopRequested) return;

            string? secretKey = _applicationState.Settings.PlayitPartnerConnection?.AgentSecretKey;
            if (string.IsNullOrWhiteSpace(secretKey)) return;

            if (State is PlayitAgentState.Connected or PlayitAgentState.Starting or PlayitAgentState.Error)
            {
                _logger.LogInformation("[PlayitAgentService] Auto-recovering Playit agent after {Reason} (waiting {Delay}s for interface readiness).", reason, delaySeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    if (!_manualStopRequested)
                    {
                        await RestartAsync(delayMs: 500);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PlayitAgentService] Auto-recovery restart failed after {Reason}.", reason);
                }
            }
        }

        public void Dispose()
        {
            UnsubscribeSystemEvents();
            lock (_networkEventLock)
            {
                _networkChangeCancellation?.Cancel();
                _networkChangeCancellation?.Dispose();
                _networkChangeCancellation = null;
            }
            _processManager.OnOutputLineReceived -= OnProcessOutput;
            _processManager.OnErrorLineReceived -= OnProcessError;
            _processManager.OnProcessExited -= OnProcessExitedCore;
            _processManager.Dispose();
            _downloadCancellation?.Cancel();
            _downloadCancellation?.Dispose();
        }
    }
}

