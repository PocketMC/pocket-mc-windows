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
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Instances.Providers;

namespace PocketMC.Desktop.Features.Mods
{
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
        private readonly InstanceManager _instanceManager;
        private readonly ModpackParser _parser;
        private readonly ILogger<ModpackService> _logger;

        public ModpackService(
            HttpClient httpClient,
            DownloaderService downloader,
            FabricProvider fabricProvider,
            ForgeProvider forgeProvider,
            InstanceManager instanceManager,
            ModpackParser parser,
            ILogger<ModpackService> logger)
        {
            _httpClient = httpClient;
            _downloader = downloader;
            _fabricProvider = fabricProvider;
            _forgeProvider = forgeProvider;
            _instanceManager = instanceManager;
            _parser = parser;
            _logger = logger;
        }

        public Task<ModpackImportResult> ParseModpackZipAsync(string zipPath)
        {
            return _parser.ParseZipAsync(zipPath);
        }

        public async Task ImportToExistingInstanceAsync(ModpackImportResult pack, InstanceMetadata metadata, string instancePath, string zipPath)
        {
            // 1. Update Instance Metadata
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
                await _fabricProvider.DownloadFabricJarAsync(pack.MinecraftVersion, pack.LoaderVersion, jarPath);
            }
            else if (pack.Loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
            {
                string forgeJarPath = Path.Combine(instancePath, "forge-installer.jar");
                await _forgeProvider.DownloadJarAsync(pack.MinecraftVersion, forgeJarPath);
            }

            // 3. Resolve and Download Mods
            await ResolveModUrlsAsync(pack);

            foreach (var mod in pack.Mods)
            {
                if (string.IsNullOrEmpty(mod.DownloadUrl) || mod.DownloadUrl.StartsWith("CURSEFORGE:")) continue;

                string? dest = PathSafety.ValidateContainedPath(instancePath, mod.DestinationPath);
                if (dest == null)
                {
                    _logger.LogWarning("Blocked mod download with path-traversal destination: {Path}", mod.DestinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                try
                {
                    await _downloader.DownloadFileAsync(mod.DownloadUrl, dest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download mod: {ModName} from {Url}", mod.Name, mod.DownloadUrl);
                }
            }

            // 4. Extract Overrides — with SEC-01 zip-slip containment
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string targetPath = "";
                if (entry.FullName.StartsWith("overrides/")) targetPath = entry.FullName.Substring(10);
                else if (entry.FullName.StartsWith("client_overrides/")) continue;

                if (string.IsNullOrEmpty(targetPath)) continue;

                string? destinationPath = PathSafety.ValidateContainedPath(instancePath, targetPath);
                if (destinationPath == null)
                {
                    _logger.LogWarning("Blocked override entry with path-traversal: {EntryName}", entry.FullName);
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
                }
            }
        }

        private async Task ResolveModUrlsAsync(ModpackImportResult pack)
        {
            var cfTasks = new List<Task>();

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
                        var response = await _httpClient.GetFromJsonAsync<JsonObject>($"https://api.curse.tools/v1/cf/mods/{projectId}/files/{fileId}");
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
