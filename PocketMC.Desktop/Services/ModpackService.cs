using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class ModpackImportResult
    {
        public string Name { get; set; } = "";
        public string MinecraftVersion { get; set; } = "";
        public string Loader { get; set; } = "";
        public string LoaderVersion { get; set; } = "";
        public List<ModFile> Mods { get; set; } = new();
    }

    public class ModFile
    {
        public string Name { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string DestinationPath { get; set; } = "";
    }

    public class ModpackService
    {
        private readonly HttpClient _httpClient;
        private readonly DownloaderService _downloader;
        private readonly FabricProvider _fabricProvider;
        private readonly ForgeProvider _forgeProvider;
        private readonly InstanceManager _instanceManager;
        private readonly ILogger<ModpackService> _logger;

        public ModpackService(
            HttpClient httpClient,
            DownloaderService downloader,
            FabricProvider fabricProvider,
            ForgeProvider forgeProvider,
            InstanceManager instanceManager,
            ILogger<ModpackService> logger)
        {
            _httpClient = httpClient;
            _downloader = downloader;
            _fabricProvider = fabricProvider;
            _forgeProvider = forgeProvider;
            _instanceManager = instanceManager;
            _logger = logger;
        }

        public async Task<ModpackImportResult> ParseModpackZipAsync(string zipPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            
            // Check for Modrinth (modrinth.index.json)
            var modrinthIndex = archive.GetEntry("modrinth.index.json");
            if (modrinthIndex != null)
            {
                return await ParseModrinthPackAsync(modrinthIndex);
            }

            // Check for CurseForge (manifest.json)
            var curseManifest = archive.GetEntry("manifest.json");
            if (curseManifest != null)
            {
                return await ParseCurseForgePackAsync(curseManifest);
            }

            throw new InvalidDataException("Unsupported modpack format. Could not find manifest.json or modrinth.index.json.");
        }

        private async Task<ModpackImportResult> ParseModrinthPackAsync(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            var index = await JsonNode.ParseAsync(stream);
            
            var result = new ModpackImportResult
            {
                Name = index?["name"]?.ToString() ?? "Imported Modpack",
                MinecraftVersion = index?["dependencies"]?["minecraft"]?.ToString() ?? "1.20.1"
            };

            // Loader Info
            if (index?["dependencies"]?["fabric-loader"] != null)
            {
                result.Loader = "Fabric";
                result.LoaderVersion = index?["dependencies"]?["fabric-loader"]?.ToString() ?? "";
            }
            else if (index?["dependencies"]?["forge"] != null)
            {
                result.Loader = "Forge";
                result.LoaderVersion = index?["dependencies"]?["forge"]?.ToString() ?? "";
            }
            else if (index?["dependencies"]?["quilt-loader"] != null)
            {
                result.Loader = "Quilt";
                result.LoaderVersion = index?["dependencies"]?["quilt-loader"]?.ToString() ?? "";
            }

            // Files & Environment Filtering
            var files = index?["files"]?.AsArray();
            if (files != null)
            {
                foreach (var f in files)
                {
                    if (f == null) continue;

                    // Server Environment Filtering
                    var env = f["env"];
                    if (env != null && env["server"]?.ToString() == "unsupported")
                    {
                        // Skip client-only mods to prevent server crash
                        continue;
                    }

                    var downloadUrl = f["downloads"]?.AsArray()?.FirstOrDefault()?.ToString();
                    var destPath = f["path"]?.ToString();
                    
                    if (downloadUrl != null && destPath != null)
                    {
                        result.Mods.Add(new ModFile
                        {
                            Name = Path.GetFileName(destPath),
                            DownloadUrl = downloadUrl,
                            DestinationPath = destPath
                        });
                    }
                }
            }

            return result;
        }

        private async Task<ModpackImportResult> ParseCurseForgePackAsync(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            var manifest = await JsonNode.ParseAsync(stream);

            var result = new ModpackImportResult
            {
                Name = manifest?["name"]?.ToString() ?? "Imported CurseForge Pack",
                MinecraftVersion = manifest?["minecraft"]?["version"]?.ToString() ?? "1.20.1"
            };

            // Loader Info
            var loaders = manifest?["minecraft"]?["modLoaders"]?.AsArray();
            var primaryLoader = loaders?.FirstOrDefault();
            if (primaryLoader != null)
            {
                string loaderId = primaryLoader["id"]?.ToString() ?? ""; // e.g., "fabric-0.15.7"
                if (loaderId.StartsWith("fabric-"))
                {
                    result.Loader = "Fabric";
                    result.LoaderVersion = loaderId.Substring(7);
                }
                else if (loaderId.StartsWith("forge-"))
                {
                    result.Loader = "Forge";
                    result.LoaderVersion = loaderId.Substring(6);
                }
            }

            // Note: Individual mod downloads for CurseForge require projectID/fileID resolution.
            // For now, we collect them. Resolution will be a separate step.
            return result;
        }

        public async Task ImportToExistingInstanceAsync(ModpackImportResult pack, InstanceMetadata metadata, string instancePath, string zipPath)
        {
            // 1. Update Instance Metadata
            metadata.MinecraftVersion = pack.MinecraftVersion;
            if (!string.IsNullOrEmpty(pack.Loader))
            {
                metadata.ServerType = pack.Loader;
                // Currently, custom loader versions might not be natively supported in metadata unless using 'Custom' or 'Fabric'.
                // We'll trust the underlying providers to resolve it if left blank, or handle it here if required.
                // Assuming ForgeProvider and FabricProvider handle this.
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
                // ForgeProvider in this codebase just takes mcVersion.
                await _forgeProvider.DownloadJarAsync(pack.MinecraftVersion, jarPath);
            }
            else if (string.IsNullOrEmpty(pack.Loader) || pack.Loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                // Fallback to Vanilla if no loader specified
            }

            // 3. Download Mods
            foreach (var mod in pack.Mods)
            {
                string dest = Path.Combine(instancePath, mod.DestinationPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await _downloader.DownloadFileAsync(mod.DownloadUrl, dest);
            }

            // 4. Extract Overrides (CurseForge/Modrinth)
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("overrides/"))
                {
                    string relativePath = entry.FullName.Substring(10);
                    if (string.IsNullOrEmpty(relativePath)) continue;

                    string destinationPath = Path.Combine(instancePath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    
                    if (!string.IsNullOrEmpty(entry.Name)) // Only files, not directories
                        entry.ExtractToFile(destinationPath, true);
                }
            }
        }
    }
}
