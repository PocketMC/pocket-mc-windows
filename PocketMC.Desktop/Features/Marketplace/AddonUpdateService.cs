using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using PocketMC.Desktop.Features.Marketplace.Models;
using PocketMC.Desktop.Models;

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
        private readonly PoggitService _poggit;
        private readonly IHttpClientFactory _httpClientFactory;

        public AddonUpdateService(
            AddonManifestService manifestService,
            ModrinthService modrinth,
            CurseForgeService curseForge,
            PoggitService poggit,
            IHttpClientFactory httpClientFactory)
        {
            _manifestService = manifestService;
            _modrinth = modrinth;
            _curseForge = curseForge;
            _poggit = poggit;
            _httpClientFactory = httpClientFactory;
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
        public async Task<AddonUpdateCheckResult> CheckForUpdateFromEntryAsync(
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
                    ProjectTitle = latestVersion.ProjectTitle
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
            EngineCompatibility compat)
        {
            if (updateInfo.LatestDownloadUrl == null || updateInfo.LatestFileName == null)
                throw new InvalidOperationException("Update info is incomplete — missing download URL or filename.");

            string safeLatestFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(updateInfo.LatestFileName);

            string destDir = Path.Combine(serverDir, compat.PrimaryAddonSubDir);
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

            // Download new file
            string newFilePath = Path.Combine(destDir, safeLatestFileName);

            using var httpClient = _httpClientFactory.CreateClient("PocketMC.Downloads");
            using var response = await httpClient.GetAsync(updateInfo.LatestDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(newFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            // Delete old file if it has a different name
            if (!string.Equals(oldFileName, safeLatestFileName, StringComparison.OrdinalIgnoreCase))
            {
                string safeOldFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(oldFileName);
                string oldFilePath = Path.Combine(destDir, safeOldFileName);
                if (File.Exists(oldFilePath))
                {
                    try { File.Delete(oldFilePath); } catch { /* Best-effort cleanup */ }
                }
            }

            // Update manifest entry
            await _manifestService.RegisterInstallAsync(
                serverDir, providerName, projectId,
                updateInfo.LatestVersionId ?? "", safeLatestFileName);
        }

        private IAddonProvider? GetProvider(string providerName)
        {
            return providerName switch
            {
                "Modrinth" => _modrinth,
                "CurseForge" => _curseForge,
                "Poggit" => _poggit,
                _ => null
            };
        }
    }
}
