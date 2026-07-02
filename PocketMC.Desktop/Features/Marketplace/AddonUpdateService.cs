using PocketMC.Desktop.Features.Marketplace.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure.Security;

namespace PocketMC.Desktop.Features.Marketplace
{
    /// <summary>
    /// Result of an update check for a single installed addon.
    /// </summary>
    public class AddonUpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string? LatestVersionId { get; set; }
        public string? LatestVersionName { get; set; }
        public string? LatestFileName { get; set; }
        public string? LatestDownloadUrl { get; set; }
        public string? ProjectTitle { get; set; }
        public string? Hash { get; set; }
        public string? HashType { get; set; }
        public string ReleaseType { get; set; } = "release";
        public List<string> Warnings { get; set; } = new();
        public string? Error { get; set; }
    }

    /// <summary>
    /// Service to check for and apply addon updates by querying source provider APIs.
    /// Works with the AddonManifestService to track installed versions and provider metadata.
    /// </summary>
    public class AddonUpdateService
    {
        private readonly AddonManifestService _manifestService;
        private readonly ModrinthService _modrinth;
        private readonly CurseForgeService _curseForge;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DownloaderService _downloader;
        private readonly MarketplaceFileInstaller _fileInstaller;
        public AddonUpdateService(
            AddonManifestService manifestService,
            ModrinthService modrinth,
            CurseForgeService curseForge,
            IHttpClientFactory httpClientFactory)
            : this(
                manifestService,
                modrinth,
                curseForge,
                httpClientFactory,
                new DownloaderService(httpClientFactory, NullLogger<DownloaderService>.Instance),
                null)
        {
        }

        public AddonUpdateService(
            AddonManifestService manifestService,
            ModrinthService modrinth,
            CurseForgeService curseForge,
            IHttpClientFactory httpClientFactory,
            DownloaderService downloader,
            MarketplaceFileInstaller? fileInstaller)
        {
            _manifestService = manifestService;
            _modrinth = modrinth;
            _curseForge = curseForge;
            _httpClientFactory = httpClientFactory;
            _downloader = downloader;
            _fileInstaller = fileInstaller ?? new MarketplaceFileInstaller(downloader, NullLogger<MarketplaceFileInstaller>.Instance);
        }

        /// <summary>
        /// Check if an update is available for a specific installed addon by its file name.
        /// Uses the addon manifest to look up provider + projectId, then queries the source API.
        /// </summary>
        public async Task<AddonUpdateCheckResult> CheckForUpdateAsync(
            string serverDir,
            string fileName,
            string mcVersion,
            string loader,
            EngineCompatibility compat)
        {
            var manifest = await _manifestService.LoadManifestAsync(serverDir);
            var entry = manifest.Entries.Find(e =>
                e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                return new AddonUpdateCheckResult
                {
                    Error = "This addon was not installed from a marketplace. Update checking is unavailable for manually imported files."
                };
            }

            return await CheckForUpdateFromEntryAsync(entry, mcVersion, loader, compat);
        }

        /// <summary>
        /// Check for update given a manifest entry (provider, projectId, installedVersionId).
        /// </summary>
        public virtual async Task<AddonUpdateCheckResult> CheckForUpdateFromEntryAsync(
            AddonManifestEntry entry,
            string mcVersion,
            string loader,
            EngineCompatibility compat)
        {
            try
            {
                IAddonProvider? provider = GetProvider(entry.Provider);
                if (provider == null)
                {
                    return new AddonUpdateCheckResult
                    {
                        Error = $"Unknown provider '{entry.Provider}'. Cannot check for updates."
                    };
                }

                // Query the provider for the latest version of this project.
                // Pass empty mcVersion/loader for engines that don't use them (Bedrock, Pocketmine).
                string mcVersionArg = (compat.Family == EngineFamily.Bedrock || compat.Family == EngineFamily.Pocketmine)
                    ? "" : (mcVersion == "*" ? "" : mcVersion);
                string loaderArg = (compat.Family == EngineFamily.Bedrock || compat.Family == EngineFamily.Pocketmine)
                    ? "" : loader;

                var latestVersion = await provider.GetLatestVersionAsync(entry.ProjectId, mcVersionArg, loaderArg);

                if (latestVersion == null)
                {
                    return new AddonUpdateCheckResult
                    {
                        Error = "Could not retrieve version information from the provider."
                    };
                }

                // Compare installed version ID with latest version ID
                bool isUpdateAvailable = !string.Equals(entry.VersionId, latestVersion.Id, StringComparison.OrdinalIgnoreCase);

                return new AddonUpdateCheckResult
                {
                    IsUpdateAvailable = isUpdateAvailable,
                    LatestVersionId = latestVersion.Id,
                    LatestVersionName = latestVersion.Name,
                    LatestFileName = latestVersion.FileName,
                    LatestDownloadUrl = latestVersion.DownloadUrl,
                    ProjectTitle = latestVersion.ProjectTitle,
                    Hash = latestVersion.Hash,
                    HashType = latestVersion.HashType,
                    ReleaseType = latestVersion.ReleaseType ?? "release",
                    Warnings = latestVersion.Warnings.ToList()
                };
            }
            catch (Exception ex)
            {
                return new AddonUpdateCheckResult
                {
                    Error = $"Update check failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Download and install an update for an addon, replacing the old file.
        /// </summary>
        public async Task ApplyUpdateAsync(
            string serverDir,
            string oldFileName,
            AddonUpdateCheckResult updateInfo,
            string providerName,
            string projectId,
            EngineCompatibility compat,
            IProgress<Instances.Services.DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (updateInfo.LatestDownloadUrl == null || updateInfo.LatestFileName == null)
                throw new InvalidOperationException("Update info is incomplete — missing download URL or filename.");

            string safeLatestFileName = MarketplaceDownloadPolicy.RequireCompatibleFileName(updateInfo.LatestFileName, compat);

            string? destDir = PathSafety.ValidateContainedPath(serverDir, compat.PrimaryAddonSubDir)
                ?? throw new InvalidOperationException($"Invalid add-on directory '{compat.PrimaryAddonSubDir}'.");
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

            string newFilePath = PathSafety.ValidateContainedPath(destDir, safeLatestFileName)
                ?? throw new InvalidOperationException($"Invalid marketplace add-on file name '{safeLatestFileName}'.");
            await _fileInstaller.InstallAsync(
                updateInfo.LatestDownloadUrl,
                newFilePath,
                updateInfo.Hash,
                updateInfo.HashType,
                progress,
                cancellationToken);

            // Delete old file if it has a different name
            if (!string.Equals(oldFileName, safeLatestFileName, StringComparison.OrdinalIgnoreCase))
            {
                string safeOldFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(oldFileName);
                string? oldFilePath = PathSafety.ValidateContainedPath(destDir, safeOldFileName);
                if (File.Exists(oldFilePath))
                {
                    try { File.Delete(oldFilePath); } catch { /* Best-effort cleanup */ }
                }
            }

            // Update manifest entry preserving or refreshing display metadata
            string? iconUrl = null;
            string? displayName = null;
            string? projectTitle = updateInfo.ProjectTitle;

            var oldManifest = await _manifestService.LoadManifestAsync(serverDir);
            var oldEntry = oldManifest.Entries.FirstOrDefault(e => e.ProjectId == projectId && e.Provider == providerName);
            if (oldEntry != null)
            {
                iconUrl = oldEntry.IconUrl;
                displayName = oldEntry.DisplayName;
                if (string.IsNullOrEmpty(projectTitle))
                {
                    projectTitle = oldEntry.ProjectTitle;
                }
            }

            await _manifestService.RegisterInstallAsync(
                serverDir, providerName, projectId,
                updateInfo.LatestVersionId ?? "", safeLatestFileName,
                projectTitle, iconUrl, displayName,
                fileHash: updateInfo.Hash,
                fileHashType: updateInfo.HashType,
                minecraftVersion: null,
                loader: compat.LoaderName,
                downloadUrl: updateInfo.LatestDownloadUrl);
        }

        private IAddonProvider? GetProvider(string providerName)
        {
            return providerName switch
            {
                "Modrinth" => _modrinth,
                "CurseForge" => _curseForge,
                _ => null
            };
        }
    }
}

