using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Mods
{
    public class ModpackImportResult
    {
        public string Name { get; set; } = "";
        public string MinecraftVersion { get; set; } = "";
        public string Loader { get; set; } = "";
        public string LoaderVersion { get; set; } = "";
        public List<ModpackFile> Mods { get; set; } = new();
    }

    public class ModpackFile
    {
        public string Name { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string DestinationPath { get; set; } = "";
    }

    /// <summary>
    /// Decoupled parser for various Minecraft modpack formats (Modrinth, CurseForge).
    /// Responsible only for reading manifest data and normalizing it.
    /// </summary>
    public sealed class ModpackParser
    {
        private readonly ILogger<ModpackParser> _logger;

        public ModpackParser(ILogger<ModpackParser> logger)
        {
            _logger = logger;
        }

        public async Task<ModpackImportResult> ParseZipAsync(string zipPath)
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
                        continue;
                    }

                    var downloadUrl = f["downloads"]?.AsArray()?.FirstOrDefault()?.ToString();
                    var destPath = f["path"]?.ToString();

                    if (downloadUrl != null && destPath != null)
                    {
                        if (PathSafety.ContainsTraversal(destPath))
                        {
                            _logger.LogWarning("Skipping mod file with suspicious path: {Path}", destPath);
                            continue;
                        }

                        result.Mods.Add(new ModpackFile
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
                string loaderId = primaryLoader["id"]?.ToString() ?? "";
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

            // Extract Mod Files (CurseForge specific indices)
            var files = manifest?["files"]?.AsArray();
            if (files != null)
            {
                foreach (var f in files)
                {
                    if (f == null) continue;

                    string projectID = f["projectID"]?.ToString() ?? "";
                    string fileID = f["fileID"]?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(projectID) && !string.IsNullOrEmpty(fileID))
                    {
                        result.Mods.Add(new ModpackFile
                        {
                            Name = $"CF-{projectID}-{fileID}",
                            DestinationPath = $"mods/{projectID}-{fileID}.jar",
                            DownloadUrl = $"CURSEFORGE:{projectID}:{fileID}"
                        });
                    }
                }
            }

            return result;
        }
    }
}
