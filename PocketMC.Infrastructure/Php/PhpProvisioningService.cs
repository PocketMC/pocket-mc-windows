using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Instances;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Php
{
    public class PhpProvisioningService
    {
        private readonly HttpClient _httpClient;
        private readonly DownloaderService _downloader;
        private readonly ApplicationState _applicationState;
        private readonly ILogger<PhpProvisioningService> _logger;

        private readonly ConcurrentDictionary<string, Task> _inflightProvisioning = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, PhpProvisioningStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

        public event Action<PhpProvisioningStatus>? OnProvisioningStatusChanged;

        public PhpProvisioningService(
            HttpClient httpClient,
            DownloaderService downloader,
            ApplicationState applicationState,
            ILogger<PhpProvisioningService> logger)
        {
            _httpClient = httpClient;
            _downloader = downloader;
            _applicationState = applicationState;
            _logger = logger;
        }

        public virtual string? GetPhpExecutablePath(string version)
        {
            if (!_applicationState.IsConfigured) return null;
            string appRoot = _applicationState.GetRequiredAppRootPath();

            // Check new consolidated runtime/php{version}
            string phpDir = Path.Combine(appRoot, "runtime", $"php{version}");
            string? found = ProbeExecutable(phpDir);
            if (found != null) return found;

            // Check legacy runtimes/php if version is 8.2 and auto-migrate
            if (version == "8.2")
            {
                string legacyPhpDir = Path.Combine(appRoot, "runtimes", "php");
                string? legacyFound = ProbeExecutable(legacyPhpDir);
                if (legacyFound != null)
                {
                    TryMigrateLegacyPhpDirectory(legacyPhpDir, phpDir);
                    return ProbeExecutable(phpDir) ?? legacyFound;
                }
            }

            return null;
        }

        private static string? ProbeExecutable(string phpDir)
        {
            if (!Directory.Exists(phpDir)) return null;

            string p1 = Path.Combine(phpDir, "bin", "php", "php.exe");
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine(phpDir, "bin", "php.exe");
            if (File.Exists(p2)) return p2;

            string p3 = Path.Combine(phpDir, "php.exe");
            if (File.Exists(p3)) return p3;

            try
            {
                var matches = Directory.EnumerateFiles(phpDir, "php.exe", SearchOption.AllDirectories).ToList();
                return matches.FirstOrDefault(m => m.IndexOf(@"\bin\php\php.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? matches.FirstOrDefault(m => m.IndexOf(@"\bin\php.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? matches.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void TryMigrateLegacyPhpDirectory(string sourceDir, string targetDir)
        {
            try
            {
                if (Directory.Exists(sourceDir) && !Directory.Exists(targetDir))
                {
                    string targetParent = Path.GetDirectoryName(targetDir)!;
                    Directory.CreateDirectory(targetParent);
                    Directory.Move(sourceDir, targetDir);
                    _logger.LogInformation("Successfully migrated legacy PHP runtime from {Source} to {Target}.", sourceDir, targetDir);

                    string legacyParent = Path.GetDirectoryName(sourceDir)!;
                    if (Directory.Exists(legacyParent) && !Directory.EnumerateFileSystemEntries(legacyParent).Any())
                    {
                        Directory.Delete(legacyParent, false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not migrate legacy PHP folder from {Source} to {Target}.", sourceDir, targetDir);
            }
        }

        public virtual bool IsPhpVersionPresent(string version)
        {
            return GetPhpExecutablePath(version) != null;
        }

        public virtual bool IsPhpPresent()
        {
            return IsPhpVersionPresent(PhpRuntimeResolver.DefaultPhpVersion);
        }

        public virtual IReadOnlyList<PhpProvisioningStatus> GetStatuses()
        {
            return PhpRuntimeResolver.GetReleaseDefinitions()
                .Select(def => GetStatus(def.Version))
                .ToList();
        }

        public PhpProvisioningStatus GetStatus(string version)
        {
            if (_statuses.TryGetValue(version, out var status))
            {
                bool actualInstalled = IsPhpVersionPresent(version);
                if (status.IsInstalled != actualInstalled && !status.IsBusy)
                {
                    status = CreateDefaultStatus(version);
                    _statuses[version] = status;
                }
                return status;
            }
            return CreateDefaultStatus(version);
        }

        private PhpProvisioningStatus CreateDefaultStatus(string version)
        {
            var def = PhpRuntimeResolver.GetDefinition(version);
            bool installed = IsPhpVersionPresent(version);
            return new PhpProvisioningStatus
            {
                Version = version,
                DisplayName = def?.DisplayName ?? $"PHP {version}",
                IsInstalled = installed,
                ExecutablePath = GetPhpExecutablePath(version),
                Stage = installed ? PhpProvisioningStage.Ready : PhpProvisioningStage.Idle,
                Message = installed ? "Installed" : "Not installed",
                ProgressPercentage = installed ? 100 : 0
            };
        }

        public async Task EnsureBundledRuntimesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var version in PhpRuntimeResolver.GetBundledPhpVersions())
            {
                if (!IsPhpVersionPresent(version))
                {
                    await EnsurePhpVersionAsync(version, null, cancellationToken);
                }
            }
        }

        public Task EnsurePhpAsync(IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return EnsurePhpVersionAsync(PhpRuntimeResolver.DefaultPhpVersion, progress, cancellationToken);
        }

        public async Task EnsurePhpVersionAsync(
            string version,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (IsPhpVersionPresent(version))
            {
                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = PhpRuntimeResolver.GetDefinition(version)?.DisplayName ?? $"PHP {version}",
                    IsInstalled = true,
                    ExecutablePath = GetPhpExecutablePath(version),
                    Stage = PhpProvisioningStage.Ready,
                    Message = "Installed",
                    ProgressPercentage = 100
                });
                return;
            }

            Task task;
            lock (_inflightProvisioning)
            {
                if (_inflightProvisioning.TryGetValue(version, out var existingTask))
                {
                    task = existingTask;
                }
                else
                {
                    task = ProvisionInternalAsync(version, progress, cancellationToken);
                    _inflightProvisioning[version] = task;
                }
            }

            try
            {
                await task;
            }
            finally
            {
                lock (_inflightProvisioning)
                {
                    _inflightProvisioning.TryRemove(version, out _);
                }
            }
        }

        private async Task ProvisionInternalAsync(
            string version,
            IProgress<DownloadProgress>? callerProgress,
            CancellationToken cancellationToken)
        {
            var def = PhpRuntimeResolver.GetDefinition(version);
            string displayName = def?.DisplayName ?? $"PHP {version}";
            string appRoot = _applicationState.GetRequiredAppRootPath();
            string runtimeDir = Path.Combine(appRoot, "runtime");
            string phpDir = Path.Combine(runtimeDir, $"php{version}");
            string tempZipPath = Path.Combine(runtimeDir, $"php{version}_temp.zip");

            Directory.CreateDirectory(runtimeDir);

            try
            {
                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = displayName,
                    Stage = PhpProvisioningStage.ResolvingPackage,
                    Message = "Resolving PHP download package..."
                });

                string? downloadUrl = null;
                if (def != null && !string.IsNullOrWhiteSpace(def.Tag))
                {
                    try
                    {
                        string baseReleases = PocketMC.Infrastructure.Configuration.AppConfig.ProviderPhpReleases;
                        var response = await _httpClient.GetFromJsonAsync<JsonObject>(
                            $"{baseReleases}/tags/{def.Tag}", cancellationToken);
                        var assets = response?["assets"] as JsonArray;
                        if (assets != null)
                        {
                            var matchedAsset = assets.FirstOrDefault(a =>
                                a is JsonObject aObj &&
                                aObj["name"]?.ToString().Contains(def.AssetPattern, StringComparison.OrdinalIgnoreCase) == true &&
                                aObj["name"]?.ToString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true &&
                                aObj["name"]?.ToString().Contains("symbol", StringComparison.OrdinalIgnoreCase) != true &&
                                aObj["name"]?.ToString().Contains("debug", StringComparison.OrdinalIgnoreCase) != true) as JsonObject;

                            downloadUrl = matchedAsset?["browser_download_url"]?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve PHP release via GitHub API for {Tag}. Falling back to direct URL.", def.Tag);
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl) && def != null)
                {
                    downloadUrl = def.FallbackDownloadUrl;
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new InvalidOperationException($"Could not determine download URL for PHP version {version}.");
                }

                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = displayName,
                    Stage = PhpProvisioningStage.Downloading,
                    Message = "Downloading PHP binary package...",
                    ProgressPercentage = 0
                });

                var progressRelay = new Progress<DownloadProgress>(p =>
                {
                    callerProgress?.Report(p);
                    UpdateStatus(new PhpProvisioningStatus
                    {
                        Version = version,
                        DisplayName = displayName,
                        Stage = PhpProvisioningStage.Downloading,
                        Message = $"Downloading PHP... {p.Percentage:0}%",
                        ProgressPercentage = p.Percentage
                    });
                });

                await _downloader.DownloadFileAsync(downloadUrl, tempZipPath, null, progressRelay, cancellationToken);

                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = displayName,
                    Stage = PhpProvisioningStage.Extracting,
                    Message = "Extracting PHP binary package...",
                    ProgressPercentage = 95
                });

                if (Directory.Exists(phpDir))
                {
                    Directory.Delete(phpDir, true);
                }

                await _downloader.ExtractZipAsync(tempZipPath, phpDir, null);

                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = displayName,
                    Stage = PhpProvisioningStage.Verifying,
                    Message = "Verifying PHP executable..."
                });

                string? exePath = ProbeExecutable(phpDir);
                if (exePath == null || !File.Exists(exePath))
                {
                    throw new FileNotFoundException($"Extracted PHP runtime does not contain a valid php.exe in {phpDir}.");
                }

                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = displayName,
                    IsInstalled = true,
                    ExecutablePath = exePath,
                    Stage = PhpProvisioningStage.Ready,
                    Message = "Installed",
                    ProgressPercentage = 100
                });

                _logger.LogInformation("Successfully provisioned PHP {Version} to {Path}", version, exePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to provision PHP runtime version {Version}.", version);
                UpdateStatus(new PhpProvisioningStatus
                {
                    Version = version,
                    DisplayName = displayName,
                    Stage = PhpProvisioningStage.Failed,
                    Message = $"Failed: {ex.Message}"
                });
                throw;
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { }
                }
            }
        }

        public async Task DeletePhpVersionAsync(string version)
        {
            if (!_applicationState.IsConfigured) return;
            string appRoot = _applicationState.GetRequiredAppRootPath();
            string phpDir = Path.Combine(appRoot, "runtime", $"php{version}");

            if (Directory.Exists(phpDir))
            {
                await Task.Run(() =>
                {
                    try
                    {
                        Directory.Delete(phpDir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete PHP runtime directory {Path}.", phpDir);
                        throw;
                    }
                });
            }

            UpdateStatus(new PhpProvisioningStatus
            {
                Version = version,
                DisplayName = PhpRuntimeResolver.GetDefinition(version)?.DisplayName ?? $"PHP {version}",
                IsInstalled = false,
                ExecutablePath = null,
                Stage = PhpProvisioningStage.Idle,
                Message = "Not installed",
                ProgressPercentage = 0
            });
        }

        private void UpdateStatus(PhpProvisioningStatus status)
        {
            _statuses[status.Version] = status;
            OnProvisioningStatusChanged?.Invoke(status);
        }
    }
}
