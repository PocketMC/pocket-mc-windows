using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Java
{
    /// <summary>
    /// Orchestrates the provisioning of Java runtimes.
    /// Delegates external API calls to JavaAdoptiumClient and 
    /// runtime verification to JavaRuntimeValidator.
    /// </summary>
    public class JavaProvisioningService
    {
        private readonly DownloaderService _downloader;
        private readonly ApplicationState _applicationState;
        private readonly JavaAdoptiumClient _adoptiumClient;
        private readonly JavaRuntimeValidator _validator;
        private readonly ILogger<JavaProvisioningService> _logger;

        private readonly ConcurrentDictionary<int, Task> _inflightProvisioning = new();
        private readonly ConcurrentDictionary<int, JavaProvisioningStatus> _statuses = new();
        private readonly ConcurrentDictionary<int, DateTimeOffset> _automaticRetryBlockedUntil = new();
        private readonly object _backgroundProvisioningLock = new();
        private Task? _backgroundProvisioningTask;

        public event Action<JavaProvisioningStatus>? OnProvisioningStatusChanged;

        public JavaProvisioningService(
            DownloaderService downloader,
            ApplicationState applicationState,
            JavaAdoptiumClient adoptiumClient,
            JavaRuntimeValidator validator,
            ILogger<JavaProvisioningService> logger)
        {
            _downloader = downloader;
            _applicationState = applicationState;
            _adoptiumClient = adoptiumClient;
            _validator = validator;
            _logger = logger;
        }

        public virtual bool IsJavaVersionPresent(int version)
        {
            string appRoot = _applicationState.GetRequiredAppRootPath();
            string runtimeDir = Path.Combine(appRoot, "runtime", $"java{version}");
            return _validator.IsRuntimePresent(runtimeDir);
        }

        public virtual IReadOnlyList<JavaProvisioningStatus> GetStatuses()
        {
            return JavaRuntimeResolver.GetBundledJavaVersions()
                .Select(GetStatus)
                .OrderBy(status => status.Version)
                .ToList();
        }

        public JavaProvisioningStatus GetStatus(int version)
        {
            return _statuses.TryGetValue(version, out var status) ? status : CreateDefaultStatus(version);
        }

        public Task EnsureBundledRuntimesAsync(CancellationToken cancellationToken = default)
        {
            return EnsureVersionsAsync(JavaRuntimeResolver.GetBundledJavaVersions(), ignoreAutomaticRetryCooldown: true, cancellationToken);
        }

        public void StartBackgroundProvisioning()
        {
            if (!_applicationState.IsConfigured) return;

            lock (_backgroundProvisioningLock)
            {
                if (_backgroundProvisioningTask is { IsCompleted: false }) return;
                if (JavaRuntimeResolver.GetBundledJavaVersions().All(IsJavaVersionPresent)) return;

                _backgroundProvisioningTask = Task.Run(async () =>
                {
                    try { await EnsureVersionsAsync(JavaRuntimeResolver.GetBundledJavaVersions(), ignoreAutomaticRetryCooldown: false, CancellationToken.None); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Background Java provisioning incomplete."); }
                });
            }
        }

        public virtual async Task EnsureJavaAsync(int version, CancellationToken cancellationToken = default)
        {
            if (IsJavaVersionPresent(version))
            {
                PublishStatus(version, JavaProvisioningStage.Ready, "Runtime is installed and ready.", 100, isInstalled: true);
                return;
            }

            Task provisioningTask = _inflightProvisioning.GetOrAdd(
                version,
                static (v, state) => state.self.ProvisionRuntimeCoreAsync(v, state.token),
                (self: this, token: cancellationToken));

            try { await provisioningTask; }
            finally
            {
                if (provisioningTask.IsCompleted)
                    _inflightProvisioning.TryRemove(new KeyValuePair<int, Task>(version, provisioningTask));
            }
        }

        private async Task EnsureVersionsAsync(IEnumerable<int> versions, bool ignoreAutomaticRetryCooldown, CancellationToken cancellationToken)
        {
            foreach (int version in versions.Distinct().OrderBy(v => v))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ignoreAutomaticRetryCooldown && ShouldSkipAutomaticRetry(version, out var blockedUntil))
                {
                    PublishAutomaticRetryDeferredStatus(version, blockedUntil);
                    continue;
                }
                await EnsureJavaAsync(version, cancellationToken);
            }
        }

        private async Task ProvisionRuntimeCoreAsync(int version, CancellationToken cancellationToken)
        {
            if (IsJavaVersionPresent(version))
            {
                PublishStatus(version, JavaProvisioningStage.Ready, "Runtime is already installed.", 100, isInstalled: true);
                return;
            }

            string appRootPath = _applicationState.GetRequiredAppRootPath();
            string runtimeDir = Path.Combine(appRootPath, "runtime");
            string tempZipPath = Path.Combine(runtimeDir, $"temp_java{version}.zip");
            string extractPath = Path.Combine(runtimeDir, $"java{version}_ext");
            string finalPath = Path.Combine(runtimeDir, $"java{version}");

            Directory.CreateDirectory(runtimeDir);
            const int maxAttempts = 4;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishStatus(version, JavaProvisioningStage.Queued, $"Preparing Java {version} (attempt {attempt}/{maxAttempts})...", 0);

                try
                {
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: false);
                    PublishStatus(version, JavaProvisioningStage.ResolvingPackage, "Resolving package metadata...", 0);

                    var package = await _adoptiumClient.ResolveRuntimePackageAsync(version, cancellationToken);

                    var downloadProgress = new Progress<DownloadProgress>(p =>
                        PublishStatus(version, JavaProvisioningStage.Downloading, $"Downloading... {FormatSize(p.BytesRead)} / {FormatSize(p.TotalBytes)}", p.Percentage));

                    await _downloader.DownloadFileAsync(package.Url, tempZipPath, package.Sha256, downloadProgress, cancellationToken);

                    PublishStatus(version, JavaProvisioningStage.Extracting, "Extracting runtime archive...", 0);
                    await _downloader.ExtractZipAsync(tempZipPath, extractPath, new Progress<DownloadProgress>(p =>
                        PublishStatus(version, JavaProvisioningStage.Extracting, "Extracting...", p.Percentage)));

                    string extractedRoot = ResolveExtractedRuntimeRoot(version, extractPath);
                    TryDeleteDirectory(finalPath);
                    Directory.Move(extractedRoot, finalPath);
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: true);

                    PublishStatus(version, JavaProvisioningStage.Verifying, "Verifying installation...", 90);
                    await _validator.ValidateRuntimeAsync(finalPath, cancellationToken);

                    _automaticRetryBlockedUntil.TryRemove(version, out _);
                    PublishStatus(version, JavaProvisioningStage.Ready, $"Java {version} installed successfully.", 100, isInstalled: true);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts && _adoptiumClient.IsRetryable(ex))
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Java {Version} provisioning attempt {Attempt} failed.", version, attempt);
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: false);
                    await Task.Delay(_adoptiumClient.GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: false);
                    break;
                }
            }

            string userMessage = $"Failed to provision Java {version}. Retries exhausted.";
            _automaticRetryBlockedUntil[version] = DateTimeOffset.UtcNow.AddMinutes(10);
            PublishStatus(version, JavaProvisioningStage.Failed, userMessage, 0);
            _logger.LogError(lastException, "Failed to provision Java {Version}.", version);
            throw new InvalidOperationException(userMessage, lastException);
        }

        private bool ShouldSkipAutomaticRetry(int version, out DateTimeOffset blockedUntil) =>
            _automaticRetryBlockedUntil.TryGetValue(version, out blockedUntil) && blockedUntil > DateTimeOffset.UtcNow && !IsJavaVersionPresent(version);

        private void PublishAutomaticRetryDeferredStatus(int version, DateTimeOffset blockedUntil)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((blockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            PublishStatus(version, JavaProvisioningStage.Failed, $"Automatic retry paused for Java {version}. Resumes in ~{minutes} min.", 0, isInstalled: false);
        }

        private string ResolveExtractedRuntimeRoot(int version, string extractPath)
        {
            var subDirs = Directory.GetDirectories(extractPath);
            if (subDirs.Length != 1) throw new InvalidOperationException($"Unexpected structure for Java {version}.");
            return subDirs[0];
        }

        private JavaProvisioningStatus CreateDefaultStatus(int version)
        {
            bool installed = IsJavaVersionPresent(version);
            return new JavaProvisioningStatus { Version = version, Stage = installed ? JavaProvisioningStage.Ready : JavaProvisioningStage.Idle, Message = installed ? "Ready" : "Missing", ProgressPercentage = installed ? 100 : 0, IsInstalled = installed };
        }

        private void PublishStatus(int version, JavaProvisioningStage stage, string message, double percentage, bool? isInstalled = null)
        {
            var status = new JavaProvisioningStatus { Version = version, Stage = stage, Message = message, ProgressPercentage = Math.Clamp(percentage, 0, 100), IsInstalled = isInstalled ?? IsJavaVersionPresent(version), UpdatedAtUtc = DateTime.UtcNow };
            _statuses[version] = status;
            OnProvisioningStatusChanged?.Invoke(status);
        }

        private void CleanupProvisioningPaths(string tempZipPath, string extractPath, string finalPath, bool keepFinalIfValid)
        {
            TryDeleteFile(tempZipPath);
            TryDeleteDirectory(extractPath);
            if (!keepFinalIfValid) TryDeleteDirectory(finalPath);
        }

        private void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
        private static string FormatSize(long bytes) => bytes < 1048576 ? $"{bytes / 1024.0:F1} KB" : $"{bytes / 1048576.0:F1} MB";
    }
}
