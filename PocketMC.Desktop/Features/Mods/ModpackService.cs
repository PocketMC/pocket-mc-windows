using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Instances.ImportExport;

namespace PocketMC.Desktop.Features.Mods
{
    public class ModpackImportResultReport
    {
        public bool Success { get; set; }
        public List<ModpackImportModEntry> Mods { get; set; } = new();
        public int ExtractedOverrideCount { get; set; }
        public List<ModpackSkippedOverride> SkippedOverrides { get; set; } = new();
    }

    public class ModpackImportModEntry
    {
        public string Name { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Orchestrates modpack imports by coordinating parsing, 
    /// loader provisioning, and file downloads.
    /// Extracts parsing logic to ModpackParser for cleaner separation.
    /// </summary>
    public class ModpackService
    {
        private readonly HttpClient _httpClient;
        private readonly DownloaderService _downloader;
        private readonly FabricProvider _fabricProvider;
        private readonly ForgeProvider _forgeProvider;
        private readonly NeoForgeProvider _neoForgeProvider;
        private readonly InstanceManager _instanceManager;
        private readonly ModpackParser _parser;
        private readonly AddonManifestService _manifestService;
        private readonly ApplicationState _appState;
        private readonly ILogger<ModpackService> _logger;

        public ModpackService(
            HttpClient httpClient,
            DownloaderService downloader,
            FabricProvider fabricProvider,
            ForgeProvider forgeProvider,
            NeoForgeProvider neoForgeProvider,
            InstanceManager instanceManager,
            ModpackParser parser,
            AddonManifestService manifestService,
            ApplicationState appState,
            ILogger<ModpackService> logger)
        {
            _httpClient = httpClient;
            _downloader = downloader;
            _fabricProvider = fabricProvider;
            _forgeProvider = forgeProvider;
            _neoForgeProvider = neoForgeProvider;
            _instanceManager = instanceManager;
            _parser = parser;
            _manifestService = manifestService;
            _appState = appState;
            _logger = logger;
        }

        public Task<ModpackImportResult> ParseModpackZipAsync(string zipPath)
        {
            return _parser.ParseZipAsync(zipPath);
        }

        public async Task<ModpackImportResultReport> ImportToExistingInstanceAsync(
            ModpackImportResult pack,
            InstanceMetadata metadata,
            string instancePath,
            string zipPath,
            IProgress<InstanceTransferProgress>? progress = null)
        {
            var report = new ModpackImportResultReport();
            
            // Backup metadata in case of failure
            string originalMinecraftVersion = metadata.MinecraftVersion;
            string originalServerType = metadata.ServerType;
            string originalLoaderVersion = metadata.LoaderVersion;

            try
            {
                // 1. Update Instance Metadata
                progress?.Report(new InstanceTransferProgress
                {
                    CurrentStep = "Updating instance metadata...",
                    OverallProgress = 1
                });

                metadata.MinecraftVersion = pack.MinecraftVersion;
                if (!string.IsNullOrEmpty(pack.Loader))
                {
                    metadata.ServerType = pack.Loader;
                    metadata.LoaderVersion = pack.LoaderVersion;
                }
                _instanceManager.SaveMetadata(metadata, instancePath);

                // 2. Download Loader JAR
                string jarPath = Path.Combine(instancePath, "server.jar");
                if (pack.Loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new InstanceTransferProgress
                    {
                        CurrentStep = "Downloading Fabric loader...",
                        OverallProgress = 3
                    });
                    await _fabricProvider.DownloadFabricJarAsync(pack.MinecraftVersion, pack.LoaderVersion, jarPath);
                }
                else if (pack.Loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new InstanceTransferProgress
                    {
                        CurrentStep = "Downloading Forge installer...",
                        OverallProgress = 3
                    });
                    string forgeJarPath = Path.Combine(instancePath, "forge-installer.jar");
                    await _forgeProvider.DownloadForgeJarAsync(pack.MinecraftVersion, pack.LoaderVersion, forgeJarPath);
                }
                else if (pack.Loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new InstanceTransferProgress
                    {
                        CurrentStep = "Downloading NeoForge installer...",
                        OverallProgress = 3
                    });
                    string neoforgeJarPath = Path.Combine(instancePath, "neoforge-installer.jar");
                    await _neoForgeProvider.DownloadNeoForgeJarAsync(pack.MinecraftVersion, pack.LoaderVersion, neoforgeJarPath);
                }

                // 3. Resolve and Download Mods
                progress?.Report(new InstanceTransferProgress
                {
                    CurrentStep = "Resolving mod URLs...",
                    OverallProgress = 8
                });
                await ResolveModUrlsAsync(pack);

                int totalMods = pack.Mods.Count;
                int currentModIndex = 0;

                foreach (var mod in pack.Mods)
                {
                    currentModIndex++;
                    double modProgressPercent = 10 + ((double)currentModIndex / totalMods) * 80;

                    progress?.Report(new InstanceTransferProgress
                    {
                        CurrentStep = $"Downloading mods ({currentModIndex}/{totalMods})...",
                        OverallProgress = modProgressPercent,
                        CurrentItem = mod.Name
                    });

                    if (string.IsNullOrEmpty(mod.DownloadUrl) || mod.DownloadUrl.StartsWith("CURSEFORGE:"))
                    {
                        report.Mods.Add(new ModpackImportModEntry
                        {
                            Name = mod.Name,
                            Success = false,
                            ErrorMessage = "Download URL was not resolved."
                        });
                        continue;
                    }

                    string? dest = PathSafety.ValidateContainedPath(instancePath, mod.DestinationPath);
                    if (dest == null)
                    {
                        string traversalMsg = $"Blocked mod download with path-traversal: {mod.DestinationPath}";
                        _logger.LogWarning(traversalMsg);
                        report.Mods.Add(new ModpackImportModEntry
                        {
                            Name = mod.Name,
                            Success = false,
                            ErrorMessage = traversalMsg
                        });
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    try
                    {
                        await _downloader.DownloadFileAsync(mod.DownloadUrl, dest);
                        
                        // Register in AddonManifestService
                        if (!string.IsNullOrEmpty(mod.Provider) && !string.IsNullOrEmpty(mod.ProjectId) && !string.IsNullOrEmpty(mod.VersionId))
                        {
                            await _manifestService.RegisterInstallAsync(
                                instancePath,
                                mod.Provider,
                                mod.ProjectId,
                                mod.VersionId,
                                Path.GetFileName(dest),
                                projectTitle: mod.Name,
                                iconUrl: null,
                                displayName: mod.Name,
                                downloadUrl: mod.DownloadUrl
                            );
                        }

                        report.Mods.Add(new ModpackImportModEntry
                        {
                            Name = mod.Name,
                            Success = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to download mod: {ModName} from {Url}", mod.Name, mod.DownloadUrl);
                        report.Mods.Add(new ModpackImportModEntry
                        {
                            Name = mod.Name,
                            Success = false,
                            ErrorMessage = ex.Message
                        });
                    }
                }

                // 4. Extract safe overrides only.
                progress?.Report(new InstanceTransferProgress
                {
                    CurrentStep = "Extracting overrides...",
                    OverallProgress = 95
                });
                pack.OverrideExtractionResult = await ExtractOverridesAsync(zipPath, instancePath);
                
                report.ExtractedOverrideCount = pack.OverrideExtractionResult.ExtractedOverrideCount;
                report.SkippedOverrides.AddRange(pack.OverrideExtractionResult.SkippedOverrides);
                
                progress?.Report(new InstanceTransferProgress
                {
                    CurrentStep = "Completed",
                    OverallProgress = 100
                });

                report.Success = true;
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Modpack import failed. Rolling back metadata.");
                
                // Rollback metadata on error
                metadata.MinecraftVersion = originalMinecraftVersion;
                metadata.ServerType = originalServerType;
                metadata.LoaderVersion = originalLoaderVersion;
                _instanceManager.SaveMetadata(metadata, instancePath);

                throw;
            }
        }

        public async Task<ModpackOverrideExtractionResult> ExtractOverridesAsync(string zipPath, string instancePath)
        {
            var result = new ModpackOverrideExtractionResult();
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string targetPath = "";
                if (entry.FullName.StartsWith("overrides/")) targetPath = entry.FullName.Substring(10);
                else if (entry.FullName.StartsWith("client_overrides/")) continue;

                if (string.IsNullOrEmpty(targetPath)) continue;

                if (!ModpackOverridePolicy.TryValidate(targetPath, out string normalizedTargetPath, out string reason))
                {
                    result.SkippedOverrides.Add(new ModpackSkippedOverride(targetPath.Replace('\\', '/'), reason));
                    _logger.LogWarning("Skipped unsafe modpack override {EntryName}: {Reason}", entry.FullName, reason);
                    continue;
                }

                string? destinationPath = PathSafety.ValidateContainedPath(instancePath, normalizedTargetPath);
                if (destinationPath == null)
                {
                    const string containmentReason = "Override path escapes the instance root.";
                    result.SkippedOverrides.Add(new ModpackSkippedOverride(normalizedTargetPath, containmentReason));
                    _logger.LogWarning("Skipped unsafe modpack override {EntryName}: {Reason}", entry.FullName, containmentReason);
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, true);
                    result.ExtractedOverrideCount++;
                }
            }

            return result;
        }

        private async Task ResolveModUrlsAsync(ModpackImportResult pack)
        {
            var cfTasks = new List<Task>();
            string? apiKey = _appState.Settings?.CurseForgeApiKey;

            foreach (var mod in pack.Mods.Where(m => m.DownloadUrl.StartsWith("CURSEFORGE:")))
            {
                cfTasks.Add(Task.Run(async () =>
                {
                    var parts = mod.DownloadUrl.Split(':');
                    if (parts.Length < 3) return;

                    string projectId = parts[1];
                    string fileId = parts[2];

                    try
                    {
                        JsonObject? response = null;
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            try
                            {
                                var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}");
                                request.Headers.Add("x-api-key", apiKey);
                                var httpResponse = await _httpClient.SendAsync(request);
                                if (httpResponse.IsSuccessStatusCode)
                                {
                                    response = await httpResponse.Content.ReadFromJsonAsync<JsonObject>();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Official CurseForge API resolution failed for project {ProjectId} file {FileId}. Falling back to proxy.", projectId, fileId);
                            }
                        }

                        if (response == null)
                        {
                            response = await _httpClient.GetFromJsonAsync<JsonObject>($"https://api.curse.tools/v1/cf/mods/{projectId}/files/{fileId}");
                        }

                        string? downloadUrl = response?["data"]?["downloadUrl"]?.ToString();
                        string? fileName = response?["data"]?["fileName"]?.ToString();

                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            mod.DownloadUrl = downloadUrl;
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                mod.Name = fileName;
                                mod.DestinationPath = $"mods/{fileName}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to resolve CurseForge mod project {ProjectId} file {FileId}", projectId, fileId);
                    }
                }));
            }

            if (cfTasks.Any())
            {
                await Task.WhenAll(cfTasks);
            }
        }
    }
}
