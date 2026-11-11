using PocketMC.Application.Interfaces.Instances;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Instances
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
                    detectedType = DetectServerType(folderPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during server type detection in '{FolderPath}'", folderPath);
                }

                // 2. Try to detect Minecraft version
                string detectedVersion = string.Empty;
                try
                {
                    var allFiles = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                        .Select(f => new { Path = f, Name = Path.GetFileName(f) })
                        .Where(f => !f.Name.Equals("bedrock_server.exe", StringComparison.OrdinalIgnoreCase) &&
                                    !f.Name.Equals("bedrock_server", StringComparison.OrdinalIgnoreCase) &&
                                    !f.Name.Equals("PocketMine-MP.phar", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    bool IsPrimaryJar(string name)
                    {
                        if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                            return false;

                        string lowerName = name.ToLowerInvariant();
                        if (lowerName.Contains("server") || 
                            lowerName.Contains("minecraft") || 
                            lowerName.Contains("launch") || 
                            lowerName.Contains("loader"))
                        {
                            return true;
                        }

                        if (!string.IsNullOrEmpty(detectedType))
                        {
                            string lowerType = detectedType.ToLowerInvariant();
                            if (lowerName.Contains(lowerType))
                            {
                                return true;
                            }
                        }

                        return false;
                    }

                    var orderedFiles = allFiles
                        .OrderBy(f =>
                        {
                            if (IsPrimaryJar(f.Name)) return 0;
                            if (f.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) return 1;
                            return 2;
                        })
                        .ToList();

                    // Pass 1: Look for strong matches (Rule A, Rule B, Rule D)
                    foreach (var file in orderedFiles)
                    {
                        // A. Try to find version immediately following "mc." or "mc-" or "mc_" (e.g. fabric-server-mc.26.2)
                        var mcMatch = Regex.Match(file.Name, @"mc[._-](\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
                        if (mcMatch.Success)
                        {
                            detectedVersion = mcMatch.Groups[1].Value;
                            break;
                        }

                        // B. Try to find version following a known brand suffix (e.g. paper-26.1.2, bedrock-server-1.26.33.1.zip)
                        var brandMatch = Regex.Match(file.Name, @"(?:paper|purpur|spigot|forge|neoforge|mohist|arclight|magma|cat(?:server)?|fabric|vanilla|bds|bedrock|pocketmine)[._-](\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
                        if (brandMatch.Success)
                        {
                            detectedVersion = brandMatch.Groups[1].Value;
                            break;
                        }

                        // D. Try to inspect inside the jar for version.json (e.g. Vanilla server.jar)
                        if (file.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using (var archive = ZipFile.OpenRead(file.Path))
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
                                _logger.LogDebug(ex, "Failed to read zip archive or parse version.json from file '{File}'", file.Path);
                            }
                        }
                    }

                    // Pass 2: Fallback (Rule C) on ordered files (Primary jars, other jars, other files)
                    if (string.IsNullOrWhiteSpace(detectedVersion))
                    {
                        foreach (var file in orderedFiles)
                        {
                            var matches = Regex.Matches(file.Name, @"\d+(?:\.\d+)+");
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

        // Jar name brands, ordered so a hybrid (Mohist/Arclight) wins over the Forge it embeds.
        private static readonly (string Token, string ServerType)[] JarBrands =
        {
            ("mohist", "Mohist"),
            ("arclight", "Arclight"),
            ("magma", "Magma"),
            ("catserver", "CatServer"),
            ("purpur", "Purpur"),
            ("spigot", "Spigot"),
            ("craftbukkit", "Spigot"),
            ("paper", "Paper"),
            ("neoforge", "NeoForge"),
            ("fabric", "Fabric"),
            ("forge", "Forge")
        };

        private static string DetectServerType(string folderPath)
        {
            if (File.Exists(Path.Combine(folderPath, "PocketMine-MP.phar")))
            {
                return "Pocketmine";
            }

            if (File.Exists(Path.Combine(folderPath, "bedrock_server.exe")) ||
                File.Exists(Path.Combine(folderPath, "bedrock_server")))
            {
                return "Bedrock";
            }

            string? jarBrand = DetectJarBrand(folderPath);
            if (jarBrand != null)
            {
                return jarBrand;
            }

            if (Directory.Exists(Path.Combine(folderPath, ".fabric")))
            {
                return "Fabric";
            }

            if (Directory.Exists(Path.Combine(folderPath, "libraries", "net", "neoforged")))
            {
                return "NeoForge";
            }

            if (Directory.Exists(Path.Combine(folderPath, "libraries", "net", "minecraftforge")))
            {
                return "Forge";
            }

            string modsPath = Path.Combine(folderPath, "mods");
            if (Directory.Exists(modsPath) &&
                Directory.GetFiles(modsPath, "*fabric*.jar", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return "Fabric";
            }

            if (Directory.Exists(Path.Combine(folderPath, ".paper-jar")))
            {
                return "Paper";
            }

            return "Vanilla";
        }

        private static string? DetectJarBrand(string folderPath)
        {
            var jarNames = Directory.GetFiles(folderPath, "*.jar", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .ToList();

            if (jarNames.Count == 0)
            {
                return null;
            }

            foreach (var (token, serverType) in JarBrands)
            {
                if (jarNames.Any(name => name!.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    return serverType;
                }
            }

            return null;
        }
    }
}
