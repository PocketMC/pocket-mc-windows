using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Infrastructure
{
    public enum UpdateStage
    {
        Idle,
        Checking,
        Downloading,
        ReadyToRestart,
        UpToDate,
        Error
    }

    public sealed class UpdateStatus
    {
        public UpdateStage Stage { get; init; }
        public string? NewVersion { get; init; }
        public double DownloadPercent { get; init; }
        public long? DownloadSize { get; init; }
        public string? ErrorMessage { get; init; }

        public static UpdateStatus From(UpdateStage stage, string? version = null,
            double percent = 0, long? size = null, string? error = null)
            => new() { Stage = stage, NewVersion = version, DownloadPercent = percent, DownloadSize = size, ErrorMessage = error };
    }

    public sealed class UpdateService : IDisposable
    {
        private const string GitHubRepo = "https://github.com/PocketMC/pocket-mc-windows";
        private const bool IncludePreReleases = false;

        private readonly ILogger<UpdateService> _logger;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private UpdateInfo? _pendingUpdate;
        private bool _disposed;

        public event Action<UpdateStatus>? OnStatusChanged;

        public UpdateStage CurrentStage { get; private set; } = UpdateStage.Idle;
        public bool HasPendingUpdate => _pendingUpdate != null;
        public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();
        public bool IsInstalled => CreateManager().IsInstalled;

        private readonly ApplicationState _appState;

        public UpdateService(ILogger<UpdateService> logger, ApplicationState appState)
        {
            _logger = logger;
            _appState = appState;
        }

        public async Task CheckAndDownloadAsync(CancellationToken ct = default)
        {
            if (_disposed) return;

            if (!await _semaphore.WaitAsync(0, ct))
            {
                _logger.LogDebug("Update check already in progress; skipping.");
                return;
            }

            try
            {
                await RunUpdateCycleAsync(ct);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void ApplyUpdateAndRestart()
        {
            if (_pendingUpdate == null)
            {
                _logger.LogWarning("ApplyUpdateAndRestart called but no pending update is staged.");
                return;
            }

            try
            {
                _logger.LogInformation("Applying Velopack update to {Version} and restarting.",
                    _pendingUpdate.TargetFullRelease?.Version);

                var mgr = CreateManager();
                mgr.ApplyUpdatesAndRestart(_pendingUpdate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply Velopack update.");
                Broadcast(UpdateStatus.From(UpdateStage.Error, error: $"Apply failed: {ex.Message}"));
            }
        }

        private async Task RunUpdateCycleAsync(CancellationToken ct)
        {
            Broadcast(UpdateStatus.From(UpdateStage.Checking));

            UpdateInfo? info;
            UpdateManager mgr;

            try
            {
                mgr = CreateManager();
                info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Velopack update check skipped or failed.");
                Broadcast(UpdateStatus.From(UpdateStage.UpToDate));
                return;
            }

            if (info == null)
            {
                _logger.LogInformation("Velopack: already on the latest version.");
                Broadcast(UpdateStatus.From(UpdateStage.UpToDate));
                return;
            }

            string newVersion = info.TargetFullRelease?.Version?.ToString() ?? "unknown";
            _logger.LogInformation("Velopack update available: {Version}. Starting background download.", newVersion);

            try
            {
                Broadcast(UpdateStatus.From(UpdateStage.Downloading, newVersion, 0, info.TargetFullRelease?.Size));

                // Fixed: DownloadUpdatesAsync now expects Action<int> instead of IProgress<int>
                await mgr.DownloadUpdatesAsync(info, pct =>
                {
                    if (!ct.IsCancellationRequested)
                        Broadcast(UpdateStatus.From(UpdateStage.Downloading, newVersion, pct, info.TargetFullRelease?.Size));
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Velopack update download was cancelled.");
                Broadcast(UpdateStatus.From(UpdateStage.Idle));
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Velopack update download failed.");
                Broadcast(UpdateStatus.From(UpdateStage.Error, newVersion, error: ex.Message));
                return;
            }

            _pendingUpdate = info;
            _logger.LogInformation("Velopack update {Version} downloaded and staged for restart.", newVersion);
            Broadcast(UpdateStatus.From(UpdateStage.ReadyToRestart, newVersion, 100, info.TargetFullRelease?.Size));

            if (_appState.Settings.AutomaticallyInstallUpdates)
            {
                _logger.LogInformation("Automatic updates are enabled. Applying update now.");
                ApplyUpdateAndRestart();
            }
        }

        private static UpdateManager CreateManager()
        {
            var source = new GithubSource(GitHubRepo, accessToken: null, IncludePreReleases);
            return new UpdateManager(source);
        }

        private void Broadcast(UpdateStatus status)
        {
            CurrentStage = status.Stage;
            try { OnStatusChanged?.Invoke(status); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
        }
    }
}
