using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
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

        public async Task<ModpackImportResultReport> ExecuteImportAsync(
            ModpackImportResult pack,
            InstanceMetadata metadata,
            string instancePath,
            string zipPath,
            IEnumerable<PocketMC.Desktop.Features.Marketplace.Models.ModDownloadTaskViewModel> uiTaskList,
            IProgress<PocketMC.Desktop.Features.Instances.ImportExport.InstanceTransferProgress>? progress = null,
            CancellationToken ct = default)
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
                metadata.IsModpack = true;
                _instanceManager.SaveMetadata(metadata, instancePath);

                // 2. Download Loader JAR
                var coreTask = uiTaskList.FirstOrDefault(t => t.IsCoreItem);
                if (coreTask != null)
                {
                    coreTask.IsDownloading = true;
                    coreTask.StatusText = "Downloading...";
                }

                string jarPath = Path.Combine(instancePath, "server.jar");
                var coreProgress = new Progress<PocketMC.Desktop.Features.Instances.Services.DownloadProgress>(p =>
                {
                    if (coreTask != null)
                    {
                        coreTask.ProgressValue = p.Percentage;
                    }
                });

                if (pack.Loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    await _fabricProvider.DownloadFabricJarAsync(pack.MinecraftVersion, pack.LoaderVersion, jarPath, coreProgress, ct);
                }
                else if (pack.Loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                {
                    string forgeJarPath = Path.Combine(instancePath, "installer.jar");
                    await _forgeProvider.DownloadForgeJarAsync(pack.MinecraftVersion, pack.LoaderVersion, forgeJarPath, coreProgress, ct);
                }
                else if (pack.Loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                {
                    string neoforgeJarPath = Path.Combine(instancePath, "installer.jar");
                    await _neoForgeProvider.DownloadNeoForgeJarAsync(pack.MinecraftVersion, pack.LoaderVersion, neoforgeJarPath, coreProgress, ct);
                }

                if (coreTask != null)
                {
                    coreTask.IsDownloading = false;
                    coreTask.StatusText = "✓ Complete";
                    coreTask.StatusForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                    coreTask.ProgressValue = 100;
                }

                // 3. Download Mods (URL Resolution is now done before this method is called)
                int totalMods = pack.Mods.Count;
                int completedMods = 0;
                if (totalMods > 0)
                {
                    progress?.Report(new InstanceTransferProgress
                    {
                        CurrentStep = $"Downloading mods (0/{totalMods})...",
                        OverallProgress = 0
                    });
                }

                var modTasks = new List<Task>();
                var semaphore = new SemaphoreSlim(8);
                var lockObj = new object();

                foreach (var mod in pack.Mods)
                {
                    modTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            ct.ThrowIfCancellationRequested();

                            var uiTask = uiTaskList.FirstOrDefault(t => !t.IsCoreItem && t.Mod == mod);
                            if (uiTask != null)
                            {
                                uiTask.IsDownloading = true;
                                uiTask.StatusText = "Downloading...";
                            }

                            if (string.IsNullOrEmpty(mod.DownloadUrl) || mod.DownloadUrl.StartsWith("CURSEFORGE:"))
                            {
                                lock (lockObj)
                                {
                                    report.Mods.Add(new ModpackImportModEntry
                                    {
                                        Name = mod.Name,
                                        Success = false,
                                        ErrorMessage = "Download URL was not resolved. The author may have disabled 3rd-party distribution."
                                    });
                                }
                                if (uiTask != null)
                                {
                                    uiTask.IsDownloading = false;
                                    uiTask.StatusText = "✗ Failed";
                                    uiTask.StatusForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                                }
                                return;
                            }

                            string? dest = PathSafety.ValidateContainedPath(instancePath, mod.DestinationPath);
                            if (dest == null)
                            {
                                string traversalMsg = $"Blocked mod download with path-traversal: {mod.DestinationPath}";
                                _logger.LogWarning(traversalMsg);
                                lock (lockObj)
                                {
                                    report.Mods.Add(new ModpackImportModEntry
                                    {
                                        Name = mod.Name,
                                        Success = false,
                                        ErrorMessage = traversalMsg
                                    });
                                }
                                if (uiTask != null)
                                {
                                    uiTask.IsDownloading = false;
                                    uiTask.StatusText = "✗ Blocked";
                                    uiTask.StatusForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                                }
                                return;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                            bool success = false;
                            string lastError = "";

                            for (int attempt = 1; attempt <= 3; attempt++)
                            {
                                try
                                {
                                    var progressHandler = new Progress<PocketMC.Desktop.Features.Instances.Services.DownloadProgress>(p =>
                                    {
                                        if (uiTask != null)
                                        {
                                            uiTask.ProgressValue = p.Percentage;
                                        }
                                    });
                                    await _downloader.DownloadFileAsync(mod.DownloadUrl, dest, null, null, progressHandler, ct);
                                    
                                    // Verify Modrinth SHA-512 Hash
                                    if (!string.IsNullOrEmpty(mod.HashSha512) && File.Exists(dest))
                                    {
                                        using var sha512 = SHA512.Create();
                                        using var fs = File.OpenRead(dest);
                                        var hashBytes = sha512.ComputeHash(fs);
                                        string hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                                        
                                        if (hashStr != mod.HashSha512.ToLowerInvariant())
                                        {
                                            throw new InvalidOperationException($"Hash mismatch for {mod.Name}. Expected {mod.HashSha512}, got {hashStr}");
                                        }
                                    }

                                    success = true;
                                    break; // Success!
                                }
                                catch (Exception ex)
                                {
                                    lastError = ex.Message;
                                    _logger.LogWarning(ex, "Attempt {Attempt} failed to download mod: {ModName} from {Url}", attempt, mod.Name, mod.DownloadUrl);
                                    if (File.Exists(dest))
                                    {
                                        try { File.Delete(dest); } catch { }
                                    }
                                    if (uiTask != null) uiTask.StatusText = $"Failed (Attempt {attempt}), Retrying...";
                                    if (attempt < 3) await Task.Delay(1000, ct); // Backoff before retry
                                }
                            }

                            if (success)
                            {
                                lock (lockObj)
                                {
                                    report.Mods.Add(new ModpackImportModEntry
                                    {
                                        Name = mod.Name,
                                        Success = true
                                    });
                                }
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

                                if (uiTask != null)
                                {
                                    uiTask.IsDownloading = false;
                                    uiTask.StatusText = "✓ Complete";
                                    uiTask.StatusForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                                    uiTask.ProgressValue = 100;
                                }

                                lock (lockObj)
                                {
                                    report.Mods.Add(new ModpackImportModEntry
                                    {
                                        Name = mod.Name,
                                        Success = true,
                                        ErrorMessage = ""
                                    });
                                }
                            }
                            else
                            {
                                lock (lockObj)
                                {
                                    report.Mods.Add(new ModpackImportModEntry
                                    {
                                        Name = mod.Name,
                                        Success = false,
                                        ErrorMessage = $"Failed after 3 attempts: {lastError}"
                                    });
                                }
                                if (uiTask != null)
                                {
                                    uiTask.IsDownloading = false;
                                    uiTask.StatusText = "✗ Failed";
                                    uiTask.StatusForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                                }
                            }
                        }
                        finally
                        {
                            var currentCompleted = Interlocked.Increment(ref completedMods);
                            if (totalMods > 0)
                            {
                                progress?.Report(new PocketMC.Desktop.Features.Instances.ImportExport.InstanceTransferProgress
                                {
                                    CurrentStep = $"Downloading mods ({currentCompleted}/{totalMods})...",
                                    OverallProgress = (double)currentCompleted / totalMods * 100.0
                                });
                            }
                            semaphore.Release();
                        }
                    }, ct));
                }

                if (modTasks.Any())
                {
                    await Task.WhenAll(modTasks);
                }

                // 4. Extract safe overrides only.
                pack.OverrideExtractionResult = await ExtractOverridesAsync(zipPath, instancePath);
                
                report.ExtractedOverrideCount = pack.OverrideExtractionResult.ExtractedOverrideCount;
                report.SkippedOverrides.AddRange(pack.OverrideExtractionResult.SkippedOverrides);
                
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

        public async Task<ModpackImportResultReport> ImportToExistingInstanceAsync(
            ModpackImportResult pack,
            InstanceMetadata metadata,
            string instancePath,
            string zipPath,
            IProgress<PocketMC.Desktop.Features.Instances.ImportExport.InstanceTransferProgress>? importProgress = null)
        {
            await ResolveModUrlsAsync(pack);
            var report = await ExecuteImportAsync(pack, metadata, instancePath, zipPath, new List<PocketMC.Desktop.Features.Marketplace.Models.ModDownloadTaskViewModel>(), importProgress, CancellationToken.None);
            return report;
        }

        public async Task<ModpackOverrideExtractionResult> ExtractOverridesAsync(string zipPath, string instancePath)
        {
            var result = new ModpackOverrideExtractionResult();
            
            ZipArchive? archive = null;
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    archive = ZipFile.OpenRead(zipPath);
                    break;
                }
                catch (IOException) when (attempt < 10)
                {
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to open modpack archive '{zipPath}': {ex.Message}", ex);
                }
            }
            
            if (archive == null) throw new IOException($"Could not open archive '{zipPath}' because it is in use.");

            using (archive)
            {
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

                    if (normalizedTargetPath.Trim().EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        const string zipReason = "Zip files are excluded from modpack installation.";
                        result.SkippedOverrides.Add(new ModpackSkippedOverride(normalizedTargetPath, zipReason));
                        _logger.LogWarning("Skipped .zip override {EntryName}: {Reason}", entry.FullName, zipReason);
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
                        bool success = false;
                        Exception? lastEx = null;
                        
                        for (int attempt = 1; attempt <= 15; attempt++) // Wait up to ~7.5 seconds
                        {
                            try 
                            {
                                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                                entry.ExtractToFile(destinationPath, true);
                                success = true;
                                break;
                            } 
                            catch (IOException ex)
                            {
                                lastEx = ex;
                                System.Threading.Thread.Sleep(500);
                            }
                            catch (UnauthorizedAccessException ex)
                            {
                                lastEx = ex;
                                System.Threading.Thread.Sleep(500);
                            }
                        }
                        
                        if (!success)
                        {
                            throw new IOException($"Failed to extract override file to '{destinationPath}'. The file might be locked by another process (like an Antivirus) or you lack permissions.", lastEx);
                        }
                        
                        result.ExtractedOverrideCount++;
                    }
                }
            }

            return result;
        }

        public async Task ResolveModUrlsAsync(ModpackImportResult pack)
        {
            var cfTasks = new List<Task>();
            string? apiKey = _appState.Settings?.CurseForgeApiKey;
            var semaphore = new SemaphoreSlim(10); // Throttle API calls

            foreach (var mod in pack.Mods.Where(m => m.DownloadUrl.StartsWith("CURSEFORGE:")))
            {
                cfTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var parts = mod.DownloadUrl.Split(':');
                        if (parts.Length < 3) return;

                        string projectId = parts[1];
                        string fileId = parts[2];

                        for (int attempt = 1; attempt <= 3; attempt++)
                        {
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

                                if (string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(fileName) && long.TryParse(fileId, out long idVal))
                                {
                                    long part1 = idVal / 1000;
                                    long part2 = idVal % 1000;
                                    downloadUrl = $"https://edge.forgecdn.net/files/{part1}/{part2:D3}/{Uri.EscapeDataString(fileName)}";
                                }

                                if (!string.IsNullOrEmpty(downloadUrl))
                                {
                                    if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        return;
                                    }

                                    mod.DownloadUrl = downloadUrl;
                                    if (!string.IsNullOrEmpty(fileName))
                                    {
                                        mod.Name = fileName;
                                        mod.DestinationPath = $"mods/{fileName}";
                                    }
                                }
                                break; // Break retry loop on success
                            }
                            catch (Exception ex)
                            {
                                if (attempt == 3)
                                {
                                    _logger.LogError(ex, "Failed to resolve CurseForge mod project {ProjectId} file {FileId} after 3 attempts.", projectId, fileId);
                                }
                                else
                                {
                                    await Task.Delay(1000 * attempt); // Backoff before retry
                                }
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
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
