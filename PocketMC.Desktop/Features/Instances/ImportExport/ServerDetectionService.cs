using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Instances.ImportExport
{
    public class ServerDetectionService : IServerDetectionService
    {
        private readonly ILogger<ServerDetectionService> _logger;

        public ServerDetectionService(ILogger<ServerDetectionService> logger)
        {
            _logger = logger;
        }

        public Task<(string ServerType, string MinecraftVersion)> DetectServerTypeAndVersionAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return Task.FromResult(("Vanilla", string.Empty));
            }

            return Task.Run(() =>
            {
                // 1. Detect server type
                string detectedType = "Vanilla";
                try
                {
                    if (File.Exists(Path.Combine(folderPath, "PocketMine-MP.phar")))
                    {
                        detectedType = "Pocketmine";
                    }
                    else if (File.Exists(Path.Combine(folderPath, "bedrock_server.exe")) || File.Exists(Path.Combine(folderPath, "bedrock_server")))
                    {
                        detectedType = "Bedrock";
                    }
                    else if (Directory.Exists(Path.Combine(folderPath, ".fabric")) || 
                             (Directory.Exists(Path.Combine(folderPath, "mods")) && 
                              Directory.GetFiles(Path.Combine(folderPath, "mods"), "*fabric*.jar", SearchOption.AllDirectories).Any()))
                    {
                        detectedType = "Fabric";
                    }
                    else if (File.Exists(Path.Combine(folderPath, "user_jvm_args.txt")) && Directory.Exists(Path.Combine(folderPath, "libraries/net/neoforged")))
                    {
                        detectedType = "NeoForge";
                    }
                    else if (File.Exists(Path.Combine(folderPath, "user_jvm_args.txt")) || Directory.Exists(Path.Combine(folderPath, "libraries/net/minecraftforge")))
                    {
                        detectedType = "Forge";
                    }
                    else if (Directory.GetFiles(folderPath, "*paper*.jar").Any() || Directory.Exists(Path.Combine(folderPath, ".paper-jar")))
                    {
                        detectedType = "Paper";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during server type detection in '{FolderPath}'", folderPath);
                }

                // 2. Try to detect Minecraft version
                string detectedVersion = string.Empty;
                try
                {
                    var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file);
                        
                        // Skip common non-version executable or launcher files
                        if (name.Equals("bedrock_server.exe", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("bedrock_server", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("PocketMine-MP.phar", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        // A. Try to find version immediately following "mc." or "mc-" or "mc_" (e.g. fabric-server-mc.26.2)
                        var mcMatch = Regex.Match(name, @"mc[._-](\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
                        if (mcMatch.Success)
                        {
                            detectedVersion = mcMatch.Groups[1].Value;
                            break;
                        }

                        // B. Try to find version following a known brand suffix (e.g. paper-26.1.2, bedrock-server-1.26.33.1.zip)
                        var brandMatch = Regex.Match(name, @"(?:paper|forge|neoforge|spigot|purpur|vanilla|bds|bedrock|pocketmine)[._-](\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
                        if (brandMatch.Success)
                        {
                            detectedVersion = brandMatch.Groups[1].Value;
                            break;
                        }

                        // C. Fallback: Find the first substring that matches a general version pattern but is not a loader version
                        var matches = Regex.Matches(name, @"\d+(?:\.\d+)+");
                        string? fallbackVersion = null;
                        foreach (Match match in matches)
                        {
                            if (!match.Value.StartsWith("0."))
                            {
                                fallbackVersion = match.Value;
                                break;
                            }
                        }
                        if (fallbackVersion != null)
                        {
                            detectedVersion = fallbackVersion;
                            break;
                        }

                        // D. Try to inspect inside the jar for version.json (e.g. Vanilla server.jar)
                        if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using (var archive = ZipFile.OpenRead(file))
                                {
                                    var entry = archive.GetEntry("version.json");
                                    if (entry != null)
                                    {
                                        using (var stream = entry.Open())
                                        using (var reader = new StreamReader(stream))
                                        {
                                            var content = reader.ReadToEnd();
                                            var idMatch = Regex.Match(content, @"""id""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                                            if (idMatch.Success)
                                            {
                                                detectedVersion = idMatch.Groups[1].Value;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Failed to read zip archive or parse version.json from file '{File}'", file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during version detection in '{FolderPath}'", folderPath);
                }

                // 3. Fallback: Try scanning the "versions" subfolder
                if (string.IsNullOrWhiteSpace(detectedVersion))
                {
                    try
                    {
                        string versionsPath = Path.Combine(folderPath, "versions");
                        if (Directory.Exists(versionsPath))
                        {
                            var subDirs = Directory.GetDirectories(versionsPath);
                            foreach (var dir in subDirs)
                            {
                                string dirName = Path.GetFileName(dir);
                                var match = Regex.Match(dirName, @"\d+(?:\.\d+)+");
                                if (match.Success)
                                {
                                    detectedVersion = match.Value;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred during subfolder version scanning in '{FolderPath}'", folderPath);
                    }
                }

                return (detectedType, detectedVersion);
            });
        }
    }
}
