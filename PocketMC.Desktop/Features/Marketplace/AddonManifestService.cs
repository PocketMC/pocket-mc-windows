using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class AddonManifestEntry
    {
        public string Provider { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime InstalledAt { get; set; }
    }

    public class AddonManifest
    {
        public List<AddonManifestEntry> Entries { get; set; } = new();
    }

    public class AddonManifestService
    {
        private const string ManifestFileName = "addon_manifest.json";

        public async Task<AddonManifest> LoadManifestAsync(string serverDir)
        {
            string path = Path.Combine(serverDir, ManifestFileName);
            if (!File.Exists(path)) return new AddonManifest();

            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<AddonManifest>(json) ?? new AddonManifest();
            }
            catch
            {
                return new AddonManifest();
            }
        }

        public async Task SaveManifestAsync(string serverDir, AddonManifest manifest)
        {
            string path = Path.Combine(serverDir, ManifestFileName);
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public async Task RegisterInstallAsync(string serverDir, string provider, string projectId, string versionId, string fileName)
        {
            var manifest = await LoadManifestAsync(serverDir);
            
            // Remove any existing entry for this project to avoid duplicates (effectively an "update")
            manifest.Entries.RemoveAll(e => e.ProjectId == projectId && e.Provider == provider);
            
            manifest.Entries.Add(new AddonManifestEntry
            {
                Provider = provider,
                ProjectId = projectId,
                VersionId = versionId,
                FileName = fileName,
                InstalledAt = DateTime.UtcNow
            });

            await SaveManifestAsync(serverDir, manifest);
        }

        public async Task UnregisterAsync(string serverDir, string provider, string projectId)
        {
            var manifest = await LoadManifestAsync(serverDir);
            int count = manifest.Entries.RemoveAll(e => e.ProjectId == projectId && e.Provider == provider);
            if (count > 0)
            {
                await SaveManifestAsync(serverDir, manifest);
            }
        }

        public async Task UnregisterByFileNameAsync(string serverDir, string fileName)
        {
            var manifest = await LoadManifestAsync(serverDir);
            int count = manifest.Entries.RemoveAll(e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                await SaveManifestAsync(serverDir, manifest);
            }
        }

        public async Task<bool> IsInstalledAsync(string serverDir, string provider, string projectId, EngineCompatibility compat)
        {
            var manifest = await LoadManifestAsync(serverDir);
            var entry = manifest.Entries.Find(e => e.ProjectId == projectId && e.Provider == provider);
            if (entry == null) return false;

            // Verify file still exists on disk
            string filePath = Path.Combine(serverDir, compat.PrimaryAddonSubDir, entry.FileName);
            
            if (!File.Exists(filePath))
            {
                // Auto-cleanup stale manifest entry
                await UnregisterAsync(serverDir, provider, projectId);
                return false;
            }

            return true;
        }

        public async Task SyncManifestAsync(string serverDir, ModrinthService modrinth, EngineCompatibility compat)
        {
            var manifest = await LoadManifestAsync(serverDir);
            bool modified = false;

            // 1. Cleanup stale entries
            var entriesToRemove = new List<AddonManifestEntry>();
            foreach (var entry in manifest.Entries)
            {
                // Better dynamic path detection based on suffix
                string subDir = (entry.FileName.EndsWith(".phar") || entry.FileName.EndsWith(".php")) ? "plugins" : 
                                (entry.FileName.EndsWith(".mcpack") || entry.FileName.EndsWith(".mcaddon")) ? "behavior_packs" : "mods";
                
                string filePath = Path.Combine(serverDir, subDir, entry.FileName);
                if (!File.Exists(filePath))
                {
                    entriesToRemove.Add(entry);
                }
            }

            if (entriesToRemove.Count > 0)
            {
                foreach (var entry in entriesToRemove) manifest.Entries.Remove(entry);
                modified = true;
            }

            // 2. Identify untracked files
            string targetDir = Path.Combine(serverDir, compat.PrimaryAddonSubDir);

            if (Directory.Exists(targetDir))
            {
                string[] extensions = compat.Family switch
                {
                    EngineFamily.Bedrock => new[] { "*.mcpack", "*.mcaddon" },
                    EngineFamily.Pocketmine => new[] { "*.phar" },
                    _ => new[] { "*.jar" }
                };

                var files = new List<string>();
                foreach(var ext in extensions)
                {
                    files.AddRange(Directory.GetFiles(targetDir, ext));
                }

                var untrackedFiles = files.Where(f => !manifest.Entries.Any(e => e.FileName == Path.GetFileName(f))).ToList();

                if (untrackedFiles.Count > 0)
                {
                    var hashToLocalPath = new Dictionary<string, string>();
                    foreach (var file in untrackedFiles)
                    {
                        try 
                        {
                            string hash = await CalculateSha1Async(file);
                            hashToLocalPath[hash] = file;
                        }
                        catch { /* Skip unreadable files */ }
                    }

                    if (hashToLocalPath.Count > 0)
                    {
                        var modrinthResults = await modrinth.GetVersionsByHashesAsync(hashToLocalPath.Keys);
                        foreach (var kvp in modrinthResults)
                        {
                            var hash = kvp.Key;
                            var version = kvp.Value;
                            if (hashToLocalPath.TryGetValue(hash, out string? localPath))
                            {
                                manifest.Entries.Add(new AddonManifestEntry
                                {
                                    Provider = "Modrinth",
                                    ProjectId = version.ProjectId,
                                    VersionId = version.Id,
                                    FileName = Path.GetFileName(localPath),
                                    InstalledAt = DateTime.UtcNow
                                });
                                modified = true;
                            }
                        }
                    }
                }
            }

            if (modified)
            {
                await SaveManifestAsync(serverDir, manifest);
            }
        }

        private async Task<string> CalculateSha1Async(string filePath)
        {
            using var sha1 = SHA1.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var hashBytes = await sha1.ComputeHashAsync(stream);
            var sb = new StringBuilder();
            foreach (var b in hashBytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
