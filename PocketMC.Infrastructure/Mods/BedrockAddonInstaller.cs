using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Services.Mods;
using PocketMC.Domain.Models;
using PocketMC.Domain.Security;
using PocketMC.Domain.Storage;

namespace PocketMC.Infrastructure.Mods
{
    /// <summary>
    /// Production-grade ingestion and management engine for Bedrock Dedicated Server (BDS)
    /// Behavior Packs and Resource Packs (.mcpack, .mcaddon, .zip).
    /// </summary>
    public sealed class BedrockAddonInstaller : IAddonManager
    {
        private const string BehaviorPacksDir = "behavior_packs";
        private const string ResourcePacksDir = "resource_packs";
        private const string WorldsDir = "worlds";
        private const string DefaultWorldName = "Bedrock level";
        private const string WorldBehaviorJson = "world_behavior_packs.json";
        private const string WorldResourceJson = "world_resource_packs.json";

        private static readonly JsonSerializerOptions IndentedJsonOptions = new()
        {
            WriteIndented = true
        };

        private static readonly JsonDocumentOptions LenientJsonDocOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly ILogger<BedrockAddonInstaller> _logger;

        public string EngineKey => "Bedrock";

        public BedrockAddonInstaller(ILogger<BedrockAddonInstaller> logger)
        {
            _logger = logger;
        }

        // ── IAddonManager Compatibility ─────────────────────────────────────

        public IReadOnlyList<AddonInfo> GetInstalledAddons(string serverDir)
        {
            var packs = GetPacks(serverDir);
            return packs.Select(p => new AddonInfo
            {
                Name = p.Name,
                FilePath = p.DirectoryPath,
                SizeKb = p.SizeKb,
                LastModified = p.LastModified,
                AddonType = p.PackType == BedrockPackType.Behavior ? "behavior" : "resource"
            }).ToList();
        }

        public async Task InstallAsync(string sourceFilePath, string serverDir, CancellationToken ct = default)
        {
            await InstallAddonAsync(sourceFilePath, serverDir, ct);
        }

        public async Task UninstallAsync(string addonPathOrId, string serverDir, CancellationToken ct = default)
        {
            var packs = GetPacks(serverDir);
            var target = packs.FirstOrDefault(p =>
                string.Equals(p.Uuid, addonPathOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.DirectoryPath, addonPathOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(p.DirectoryPath), addonPathOrId, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                await DeletePackAsync(serverDir, target.Uuid, target.PackType, ct);
            }
            else
            {
                // Fallback direct directory scrub
                await DeleteDirectoryFallbackAsync(addonPathOrId, serverDir, ct);
            }
        }

        // ── Rich BDS Pack Management API ────────────────────────────────────

        /// <summary>
        /// Retrieves all installed Behavior and Resource packs for the server,
        /// annotated with active enablement state and load order rank.
        /// </summary>
        public IReadOnlyList<BedrockPackInfo> GetPacks(string serverDir)
        {
            var result = new List<BedrockPackInfo>();
            if (!Directory.Exists(serverDir)) return result;

            string worldDir = ResolveWorldDirectory(serverDir);
            var activeBps = ReadWorldJsonEntries(Path.Combine(worldDir, WorldBehaviorJson));
            var activeRps = ReadWorldJsonEntries(Path.Combine(worldDir, WorldResourceJson));

            // 1. Collect Behavior Packs
            CollectPacksFromDirectory(
                serverDir,
                Path.Combine(serverDir, BehaviorPacksDir),
                BedrockPackType.Behavior,
                activeBps,
                result);

            // 2. Collect Resource Packs
            CollectPacksFromDirectory(
                serverDir,
                Path.Combine(serverDir, ResourcePacksDir),
                BedrockPackType.Resource,
                activeRps,
                result);

            return result;
        }

        /// <summary>
        /// Ingests a .mcpack, .mcaddon, or .zip archive, extracting any nested archives
        /// and registering all discovered Behavior and Resource packs in the active world.
        /// </summary>
        public async Task<IReadOnlyList<BedrockPackInfo>> InstallAddonAsync(
            string sourceFilePath,
            string serverDir,
            CancellationToken ct = default)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Addon file not found: {sourceFilePath}");

            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (ext is not ".mcpack" and not ".mcaddon" and not ".zip")
                throw new NotSupportedException($"Unsupported addon format: {ext}. Only .mcpack, .mcaddon, and .zip are supported.");

            string tempExtractionDir = Path.Combine(Path.GetTempPath(), $"pocketmc-bds-addon-{Guid.NewGuid():N}");
            var installedPacks = new List<BedrockPackInfo>();

            try
            {
                Directory.CreateDirectory(tempExtractionDir);
                _logger.LogInformation("Extracting Bedrock add-on {File} to {TempDir}...", Path.GetFileName(sourceFilePath), tempExtractionDir);

                // Multi-pass recursive extraction (handles nested .mcpack/.zip inside .mcaddon)
                await ExtractArchiveRecursivelyAsync(sourceFilePath, tempExtractionDir, maxDepth: 4, ct);

                // Discover all manifest.json files across the extracted directory tree
                var manifestPaths = FindManifestFiles(tempExtractionDir);
                if (manifestPaths.Count == 0)
                {
                    string diagReason = DiagnoseMissingManifest(tempExtractionDir, Path.GetFileName(sourceFilePath));
                    throw new InvalidOperationException(diagReason);
                }

                _logger.LogInformation("Discovered {Count} manifest(s) in {File}.", manifestPaths.Count, Path.GetFileName(sourceFilePath));

                foreach (var manifestPath in manifestPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    var pack = await IngestSinglePackAsync(manifestPath, serverDir, ct);
                    if (pack != null)
                    {
                        installedPacks.Add(pack);
                    }
                }

                if (installedPacks.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Could not install any valid Bedrock packs from '{Path.GetFileName(sourceFilePath)}'.");
                }

                return installedPacks;
            }
            finally
            {
                TryDeleteDirectory(tempExtractionDir);
            }
        }

        /// <summary>
        /// Enables or disables a pack in the active world's JSON registration without deleting files on disk.
        /// </summary>
        public async Task SetPackEnabledAsync(
            string serverDir,
            string packUuid,
            BedrockPackType packType,
            bool isEnabled,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverDir);
            ArgumentException.ThrowIfNullOrWhiteSpace(packUuid);

            string worldDir = ResolveWorldDirectory(serverDir);
            string jsonFilePath = packType == BedrockPackType.Behavior
                ? Path.Combine(worldDir, WorldBehaviorJson)
                : Path.Combine(worldDir, WorldResourceJson);

            var packs = GetPacks(serverDir);
            var pack = packs.FirstOrDefault(p => p.PackType == packType && string.Equals(p.Uuid, packUuid, StringComparison.OrdinalIgnoreCase));
            string version = pack?.Version ?? "1.0.0";

            if (isEnabled)
            {
                await RegisterInWorldJsonAsync(jsonFilePath, packUuid, version, ct);
            }
            else
            {
                await RemoveFromWorldJsonAsync(jsonFilePath, packUuid, ct);
            }
        }

        /// <summary>
        /// Moves a pack up or down in the active world's JSON load order array.
        /// </summary>
        public async Task ReorderPackAsync(
            string serverDir,
            string packUuid,
            BedrockPackType packType,
            bool moveUp,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverDir);
            ArgumentException.ThrowIfNullOrWhiteSpace(packUuid);

            string worldDir = ResolveWorldDirectory(serverDir);
            string jsonFilePath = packType == BedrockPackType.Behavior
                ? Path.Combine(worldDir, WorldBehaviorJson)
                : Path.Combine(worldDir, WorldResourceJson);

            if (!File.Exists(jsonFilePath)) return;

            var entries = ReadWorldJsonNode(jsonFilePath);
            if (entries == null || entries.Count < 2) return;

            int index = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i]?["pack_id"]?.GetValue<string>(), packUuid, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index == -1) return;

            int targetIndex = moveUp ? index - 1 : index + 1;
            if (targetIndex < 0 || targetIndex >= entries.Count) return;

            var item = entries[index];
            entries.RemoveAt(index);
            entries.Insert(targetIndex, item);

            await FileUtils.AtomicWriteAllTextAsync(jsonFilePath, entries.ToJsonString(IndentedJsonOptions), cancellationToken: ct);
            _logger.LogInformation("Reordered Bedrock pack {Uuid} in {File} to index {Index}.", packUuid, Path.GetFileName(jsonFilePath), targetIndex);
        }

        /// <summary>
        /// Permanently uninstalls a pack: scrubs its entry from active world JSON and deletes the directory on disk.
        /// </summary>
        public async Task DeletePackAsync(
            string serverDir,
            string packUuid,
            BedrockPackType packType,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverDir);
            ArgumentException.ThrowIfNullOrWhiteSpace(packUuid);

            // 1. Remove from world JSON
            string worldDir = ResolveWorldDirectory(serverDir);
            string jsonFilePath = packType == BedrockPackType.Behavior
                ? Path.Combine(worldDir, WorldBehaviorJson)
                : Path.Combine(worldDir, WorldResourceJson);

            await RemoveFromWorldJsonAsync(jsonFilePath, packUuid, ct);

            // 2. Find and delete directory on disk
            string subDir = packType == BedrockPackType.Behavior ? BehaviorPacksDir : ResourcePacksDir;
            string packsRoot = Path.Combine(serverDir, subDir);

            if (Directory.Exists(packsRoot))
            {
                foreach (var dir in Directory.GetDirectories(packsRoot))
                {
                    var manifestPath = Path.Combine(dir, "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        manifestPath = FindManifestFiles(dir).FirstOrDefault();
                    }

                    if (manifestPath != null && File.Exists(manifestPath))
                    {
                        var parsed = TryParseManifest(manifestPath);
                        if (parsed != null && string.Equals(parsed.Uuid, packUuid, StringComparison.OrdinalIgnoreCase))
                        {
                            await Task.Run(() => Directory.Delete(dir, recursive: true), ct);
                            _logger.LogInformation("Deleted Bedrock pack directory {Dir}.", dir);
                            break;
                        }
                    }
                }
            }
        }

        // ── Ingestion & Extraction Implementation ──────────────────────────

        private async Task ExtractArchiveRecursivelyAsync(
            string archivePath,
            string destinationDir,
            int maxDepth,
            CancellationToken ct)
        {
            if (maxDepth <= 0) return;

            await SafeZipExtractor.ExtractAsync(archivePath, destinationDir);

            // Scan for nested archives (.mcpack, .mcaddon, .zip)
            var nestedArchives = Directory.EnumerateFiles(destinationDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    return e is ".mcpack" or ".mcaddon" or ".zip";
                })
                .ToList();

            foreach (var nested in nestedArchives)
            {
                ct.ThrowIfCancellationRequested();
                string nestedExtractDir = Path.Combine(
                    Path.GetDirectoryName(nested)!,
                    $"nested_{Path.GetFileNameWithoutExtension(nested)}_{Guid.NewGuid():N}");

                try
                {
                    Directory.CreateDirectory(nestedExtractDir);
                    await ExtractArchiveRecursivelyAsync(nested, nestedExtractDir, maxDepth - 1, ct);
                    try { File.Delete(nested); } catch { }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract nested archive {Nested}.", nested);
                }
            }
        }

        private static List<string> FindManifestFiles(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory)) return new List<string>();

            return Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
                .Where(f => string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static string DiagnoseMissingManifest(string extractedDir, string fileName)
        {
            try
            {
                // 1. Check for Java Resource Pack (pack.mcmeta)
                if (Directory.EnumerateFiles(extractedDir, "pack.mcmeta", SearchOption.AllDirectories).Any())
                {
                    return $"'{fileName}' is a Minecraft Java Edition texture/resource pack ('pack.mcmeta' found). It is not compatible with Bedrock Dedicated Servers.";
                }

                // 2. Check for Java Mod (fabric.mod.json, mcmod.info, mods.toml, or .jar)
                if (Directory.EnumerateFiles(extractedDir, "*.jar", SearchOption.AllDirectories).Any() ||
                    Directory.EnumerateFiles(extractedDir, "fabric.mod.json", SearchOption.AllDirectories).Any() ||
                    Directory.EnumerateFiles(extractedDir, "mcmod.info", SearchOption.AllDirectories).Any() ||
                    Directory.EnumerateFiles(extractedDir, "mods.toml", SearchOption.AllDirectories).Any())
                {
                    return $"'{fileName}' is a Minecraft Java Edition mod. It is not compatible with Bedrock Dedicated Servers.";
                }

                // 3. Check for Minecraft World Save (level.dat)
                if (Directory.EnumerateFiles(extractedDir, "level.dat", SearchOption.AllDirectories).Any() ||
                    Directory.EnumerateDirectories(extractedDir, "db", SearchOption.AllDirectories).Any())
                {
                    return $"'{fileName}' is a Minecraft World save rather than an Add-on (Behavior/Resource Pack).";
                }

                // 4. Check for loose Bedrock assets
                bool hasBedrockFolders = Directory.EnumerateDirectories(extractedDir, "*", SearchOption.AllDirectories)
                    .Any(d =>
                    {
                        string name = Path.GetFileName(d).ToLowerInvariant();
                        return name is "entities" or "entity" or "recipes" or "loot_tables" or "textures" or "models" or "animation_controllers" or "scripts" or "attachables";
                    });

                if (hasBedrockFolders)
                {
                    return $"'{fileName}' contains add-on assets but is missing the required 'manifest.json' definition file in its package root.";
                }
            }
            catch { }

            return $"'{fileName}' is not a valid Bedrock Dedicated Server add-on (no 'manifest.json' pack definition found).";
        }

        private async Task<BedrockPackInfo?> IngestSinglePackAsync(
            string manifestPath,
            string serverDir,
            CancellationToken ct)
        {
            var manifest = TryParseManifest(manifestPath);
            if (manifest == null)
            {
                _logger.LogWarning("Skipping invalid manifest at {Path}.", manifestPath);
                return null;
            }

            string packSourceDir = Path.GetDirectoryName(manifestPath)!;
            string sanitizedName = SanitizeDirName(manifest.Name ?? "");
            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                throw new InvalidOperationException($"Pack name '{manifest.Name}' is invalid.");
            }

            bool isBehavior = manifest.PackType == BedrockPackType.Behavior;
            string targetSubDir = isBehavior ? BehaviorPacksDir : ResourcePacksDir;
            string packsRoot = PathSafety.ValidateContainedPath(serverDir, targetSubDir)
                ?? throw new InvalidOperationException("Invalid packs root directory.");
            Directory.CreateDirectory(packsRoot);

            string packDestDir = PathSafety.ValidateContainedPath(packsRoot, sanitizedName)
                ?? throw new InvalidOperationException("Invalid pack destination directory.");

            // If a directory with this name exists for a DIFFERENT pack UUID, append part of UUID to prevent collision
            if (Directory.Exists(packDestDir))
            {
                string existingManifest = Path.Combine(packDestDir, "manifest.json");
                if (File.Exists(existingManifest))
                {
                    var existing = TryParseManifest(existingManifest);
                    if (existing != null && !string.Equals(existing.Uuid, manifest.Uuid, StringComparison.OrdinalIgnoreCase))
                    {
                        packDestDir = PathSafety.ValidateContainedPath(packsRoot, $"{sanitizedName}_{manifest.Uuid[..8]}")
                            ?? throw new InvalidOperationException("Invalid collision directory.");
                    }
                }
            }

            // Copy pack files to destination
            if (Directory.Exists(packDestDir))
            {
                Directory.Delete(packDestDir, recursive: true);
            }

            await Task.Run(() => CopyDirectory(packSourceDir, packDestDir), ct);

            // Register in the active world JSON
            string worldDir = ResolveWorldDirectory(serverDir);
            string worldJson = isBehavior
                ? Path.Combine(worldDir, WorldBehaviorJson)
                : Path.Combine(worldDir, WorldResourceJson);

            await RegisterInWorldJsonAsync(worldJson, manifest.Uuid, manifest.Version, ct);

            _logger.LogInformation("Successfully installed Bedrock {Type} pack '{Name}' ({Uuid}) to {Dest}.",
                isBehavior ? "Behavior" : "Resource", manifest.Name, manifest.Uuid, packDestDir);

            string? iconPath = DiscoverPackIcon(packDestDir);
            var (resolvedName, resolvedDesc) = ResolvePackStrings(packDestDir, manifest.Name ?? sanitizedName, manifest.Description ?? "");

            return new BedrockPackInfo
            {
                Uuid = manifest.Uuid,
                Name = resolvedName,
                Description = resolvedDesc,
                Version = manifest.Version,
                MinEngineVersion = manifest.MinEngineVersion,
                PackType = manifest.PackType,
                DirectoryPath = packDestDir,
                IconPath = iconPath,
                IsEnabled = true,
                LoadOrder = 1,
                SizeKb = GetDirectorySizeBytes(packDestDir) / 1024.0,
                LastModified = DateTime.UtcNow
            };
        }

        // ── Manifest Parsing ─────────────────────────────────────────────────

        private static async Task<string?> TryReadUuidAsync(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath)) return null;

                string rawText = await File.ReadAllTextAsync(manifestPath);
                if (rawText.StartsWith("\uFEFF", StringComparison.Ordinal)) rawText = rawText[1..];

                using var doc = JsonDocument.Parse(rawText, LenientJsonDocOptions);
                var root = doc.RootElement;
                if (root.TryGetProperty("header", out var header) &&
                    header.TryGetProperty("uuid", out var uuidProp))
                {
                    string? uuid = uuidProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(uuid) && Guid.TryParse(uuid, out _))
                    {
                        return uuid;
                    }
                }
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static ManifestParseResult? TryParseManifest(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath)) return null;

                string rawText = File.ReadAllText(manifestPath, Encoding.UTF8);
                // Strip UTF-8 BOM if present
                if (rawText.StartsWith("\uFEFF", StringComparison.Ordinal))
                {
                    rawText = rawText[1..];
                }

                using var doc = JsonDocument.Parse(rawText, LenientJsonDocOptions);
                var root = doc.RootElement;

                if (!root.TryGetProperty("header", out var header))
                {
                    return null;
                }

                if (!header.TryGetProperty("uuid", out var uuidProp) ||
                    string.IsNullOrWhiteSpace(uuidProp.GetString()))
                {
                    return null;
                }

                string uuid = uuidProp.GetString()!.Trim();
                if (!Guid.TryParse(uuid, out _))
                {
                    return null;
                }

                string name = header.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string description = header.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";

                string version = "1.0.0";
                if (header.TryGetProperty("version", out var verProp))
                {
                    version = FormatVersionElement(verProp);
                }

                string minEngineVersion = "";
                if (header.TryGetProperty("min_engine_version", out var minVerProp))
                {
                    minEngineVersion = FormatVersionElement(minVerProp);
                }

                // Determine pack type from modules
                var packType = BedrockPackType.Behavior;
                if (root.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mod in modules.EnumerateArray())
                    {
                        if (mod.TryGetProperty("type", out var typeProp))
                        {
                            string typeStr = typeProp.GetString() ?? "";
                            if (string.Equals(typeStr, "resources", StringComparison.OrdinalIgnoreCase))
                            {
                                packType = BedrockPackType.Resource;
                                break;
                            }
                        }
                    }
                }

                return new ManifestParseResult(uuid, name, description, version, minEngineVersion, packType);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static string FormatVersionElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<int>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.TryGetInt32(out int val))
                        parts.Add(val);
                }
                if (parts.Count > 0)
                    return string.Join(".", parts);
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "1.0.0";
            }

            return "1.0.0";
        }

        // ── World JSON management ─────────────────────────────────────────────

        public static string ResolveActiveWorldName(string serverDir)
        {
            string propsPath = Path.Combine(serverDir, "server.properties");
            if (File.Exists(propsPath))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(propsPath))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("#") || !trimmed.Contains('=')) continue;

                        int eqIndex = trimmed.IndexOf('=');
                        string key = trimmed[..eqIndex].Trim();
                        string value = trimmed[(eqIndex + 1)..].Trim();

                        if (string.Equals(key, "level-name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
                catch { }
            }

            return DefaultWorldName;
        }

        public static string ResolveWorldDirectory(string serverDir)
        {
            string worldName = ResolveActiveWorldName(serverDir);
            string worldDir = Path.Combine(serverDir, WorldsDir, worldName);

            if (Directory.Exists(worldDir))
            {
                return worldDir;
            }

            // Fallback: search worlds directory if configured worldName doesn't exist yet
            string worldsParent = Path.Combine(serverDir, WorldsDir);
            if (Directory.Exists(worldsParent))
            {
                var existing = Directory.GetDirectories(worldsParent).FirstOrDefault();
                if (existing != null)
                {
                    return existing;
                }
            }

            Directory.CreateDirectory(worldDir);
            return worldDir;
        }

        private static List<WorldPackEntry> ReadWorldJsonEntries(string jsonFilePath)
        {
            var list = new List<WorldPackEntry>();
            if (!File.Exists(jsonFilePath)) return list;

            try
            {
                string raw = File.ReadAllText(jsonFilePath, Encoding.UTF8);
                if (raw.StartsWith("\uFEFF", StringComparison.Ordinal)) raw = raw[1..];

                using var doc = JsonDocument.Parse(raw, LenientJsonDocOptions);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    int order = 1;
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("pack_id", out var idProp))
                        {
                            string id = idProp.GetString() ?? "";
                            string ver = item.TryGetProperty("version", out var v) ? FormatVersionElement(v) : "1.0.0";
                            list.Add(new WorldPackEntry(id, ver, order++));
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        private static JsonArray? ReadWorldJsonNode(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath)) return new JsonArray();
            try
            {
                string raw = File.ReadAllText(jsonFilePath, Encoding.UTF8);
                if (raw.StartsWith("\uFEFF", StringComparison.Ordinal)) raw = raw[1..];
                return JsonNode.Parse(raw) as JsonArray ?? new JsonArray();
            }
            catch
            {
                return new JsonArray();
            }
        }

        private static async Task RegisterInWorldJsonAsync(
            string jsonFilePath,
            string uuid,
            string version,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonFilePath)!);

            var entries = ReadWorldJsonNode(jsonFilePath) ?? new JsonArray();

            // Remove any existing entry with this UUID to avoid duplicates
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entries[i]?["pack_id"]?.GetValue<string>(), uuid, StringComparison.OrdinalIgnoreCase))
                {
                    entries.RemoveAt(i);
                }
            }

            int[] verParts = ParseVersionParts(version);
            var newEntry = new JsonObject
            {
                ["pack_id"] = uuid,
                ["version"] = new JsonArray(verParts[0], verParts[1], verParts[2])
            };

            entries.Add(newEntry);

            await FileUtils.AtomicWriteAllTextAsync(jsonFilePath, entries.ToJsonString(IndentedJsonOptions), cancellationToken: ct);
        }

        private static async Task RemoveFromWorldJsonAsync(
            string jsonFilePath,
            string uuid,
            CancellationToken ct)
        {
            if (!File.Exists(jsonFilePath)) return;

            var entries = ReadWorldJsonNode(jsonFilePath);
            if (entries == null || entries.Count == 0) return;

            bool modified = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entries[i]?["pack_id"]?.GetValue<string>(), uuid, StringComparison.OrdinalIgnoreCase))
                {
                    entries.RemoveAt(i);
                    modified = true;
                }
            }

            if (modified)
            {
                await FileUtils.AtomicWriteAllTextAsync(jsonFilePath, entries.ToJsonString(IndentedJsonOptions), cancellationToken: ct);
            }
        }

        // ── Helper Methods ───────────────────────────────────────────────────

        private void CollectPacksFromDirectory(
            string serverDir,
            string packsDirectory,
            BedrockPackType packType,
            List<WorldPackEntry> activeWorldEntries,
            List<BedrockPackInfo> output)
        {
            if (!Directory.Exists(packsDirectory)) return;

            foreach (var packDir in Directory.GetDirectories(packsDirectory))
            {
                string manifestPath = Path.Combine(packDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    manifestPath = FindManifestFiles(packDir).FirstOrDefault() ?? "";
                }

                if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                    continue;

                var manifest = TryParseManifest(manifestPath);
                if (manifest == null) continue;

                string? iconPath = DiscoverPackIcon(packDir);
                var (resolvedName, resolvedDesc) = ResolvePackStrings(packDir, manifest.Name ?? Path.GetFileName(packDir), manifest.Description ?? "");
                var activeEntry = activeWorldEntries.FirstOrDefault(e =>
                    string.Equals(e.PackId, manifest.Uuid, StringComparison.OrdinalIgnoreCase));

                bool isEnabled = activeEntry != null;
                int loadOrder = activeEntry?.LoadOrder ?? -1;

                output.Add(new BedrockPackInfo
                {
                    Uuid = manifest.Uuid,
                    Name = resolvedName,
                    Description = resolvedDesc,
                    Version = manifest.Version,
                    MinEngineVersion = manifest.MinEngineVersion,
                    PackType = packType,
                    DirectoryPath = packDir,
                    IconPath = iconPath,
                    IsEnabled = isEnabled,
                    LoadOrder = loadOrder,
                    SizeKb = GetDirectorySizeBytes(packDir) / 1024.0,
                    LastModified = Directory.GetLastWriteTime(packDir)
                });
            }
        }

        private static string? DiscoverPackIcon(string packDir)
        {
            string[] candidateNames = { "pack_icon.png", "pack_icon.jpg", "pack_icon.jpeg", "icon.png" };
            foreach (var name in candidateNames)
            {
                string path = Path.Combine(packDir, name);
                if (File.Exists(path)) return path;
            }

            // Also check 1 level down
            try
            {
                foreach (var sub in Directory.GetDirectories(packDir))
                {
                    foreach (var name in candidateNames)
                    {
                        string path = Path.Combine(sub, name);
                        if (File.Exists(path)) return path;
                    }
                }
            }
            catch { }

            return null;
        }

        private static long GetDirectorySizeBytes(string dir)
        {
            try
            {
                return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
            {
                string destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(source))
            {
                string destSub = Path.Combine(dest, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSub);
            }
        }

        private static string SanitizeDirName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + ":*?\"<>|";
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c))
                .Trim()
                .TrimEnd('.');
        }

        private static (string Name, string Description) ResolvePackStrings(string packDir, string rawName, string rawDescription)
        {
            string name = rawName;
            string description = rawDescription;

            bool nameIsKey = string.IsNullOrWhiteSpace(name) || name.StartsWith("pack.", StringComparison.OrdinalIgnoreCase);
            bool descIsKey = string.IsNullOrWhiteSpace(description) || description.StartsWith("pack.", StringComparison.OrdinalIgnoreCase);

            if (nameIsKey || descIsKey)
            {
                string textsDir = Path.Combine(packDir, "texts");
                if (Directory.Exists(textsDir))
                {
                    string[] candidateLangFiles = { "en_US.lang", "en_GB.lang" };
                    string? langPath = candidateLangFiles
                        .Select(f => Path.Combine(textsDir, f))
                        .FirstOrDefault(File.Exists)
                        ?? Directory.GetFiles(textsDir, "*.lang").FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(langPath) && File.Exists(langPath))
                    {
                        try
                        {
                            foreach (var line in File.ReadAllLines(langPath))
                            {
                                string trimmed = line.Trim();
                                if (trimmed.StartsWith("#") || !trimmed.Contains('=')) continue;
                                int eq = trimmed.IndexOf('=');
                                string key = trimmed[..eq].Trim();
                                string val = trimmed[(eq + 1)..].Trim();

                                if (nameIsKey && (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) || string.Equals(key, "pack.name", StringComparison.OrdinalIgnoreCase)))
                                {
                                    name = val;
                                    nameIsKey = false;
                                }
                                if (descIsKey && (string.Equals(key, description, StringComparison.OrdinalIgnoreCase) || string.Equals(key, "pack.description", StringComparison.OrdinalIgnoreCase)))
                                {
                                    description = val;
                                    descIsKey = false;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("pack.", StringComparison.OrdinalIgnoreCase))
            {
                string folder = Path.GetFileName(packDir);
                name = !string.IsNullOrWhiteSpace(folder) ? folder.Replace('_', ' ').Replace('-', ' ') : "Bedrock Pack";
            }

            if (description.StartsWith("pack.", StringComparison.OrdinalIgnoreCase))
            {
                description = string.Empty;
            }

            return (name, description);
        }

        private static int[] ParseVersionParts(string version)
        {
            var parts = version.Split('.');
            int[] result = { 1, 0, 0 };
            for (int i = 0; i < Math.Min(3, parts.Length); i++)
            {
                if (int.TryParse(parts[i], out int v)) result[i] = v;
            }
            return result;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { }
        }

        private async Task DeleteDirectoryFallbackAsync(string addonPathOrId, string serverDir, CancellationToken ct)
        {
            foreach (var sub in new[] { BehaviorPacksDir, ResourcePacksDir })
            {
                string dir = Path.Combine(serverDir, sub, addonPathOrId);
                if (Directory.Exists(dir))
                {
                    await Task.Run(() => Directory.Delete(dir, recursive: true), ct);
                    _logger.LogInformation("Deleted pack directory {Dir}.", dir);
                    break;
                }
            }
        }

        // ── Private Records ──────────────────────────────────────────────────

        private sealed record ManifestParseResult(
            string Uuid,
            string? Name,
            string? Description,
            string Version,
            string MinEngineVersion,
            BedrockPackType PackType);

        private sealed record WorldPackEntry(string PackId, string Version, int LoadOrder);
    }
}
