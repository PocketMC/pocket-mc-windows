using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Backups;

namespace PocketMC.Desktop.Features.Mods;

/// <summary>
/// Smart local ingestion engine for Bedrock <c>.mcpack</c> and <c>.mcaddon</c> files.
///
/// Workflow for each archive:
/// <list type="number">
///   <item>Extract the archive to a temp folder.</item>
///   <item>Locate every <c>manifest.json</c> inside — one pack per manifest.</item>
///   <item>Read <c>uuid</c>, <c>version</c>, and <c>type</c> (data = behaviour pack; resources = resource pack).</item>
///   <item>Copy the pack directory to <c>[ServerRoot]/behavior_packs/</c> or <c>[ServerRoot]/resource_packs/</c>.</item>
///   <item>Register the pack in the active world's <c>world_behavior_packs.json</c> / <c>world_resource_packs.json</c>
///         so BDS actually loads it at runtime.</item>
/// </list>
/// </summary>
public sealed class BedrockAddonInstaller : IAddonManager
{
    private const string BehaviorPacksDir    = "behavior_packs";
    private const string ResourcePacksDir    = "resource_packs";
    private const string WorldsDir           = "worlds";
    private const string DefaultWorldName    = "Bedrock level";
    private const string WorldBehaviorJson   = "world_behavior_packs.json";
    private const string WorldResourceJson   = "world_resource_packs.json";

    private readonly ILogger<BedrockAddonInstaller> _logger;

    public string EngineKey => "Bedrock";

    public BedrockAddonInstaller(ILogger<BedrockAddonInstaller> logger)
    {
        _logger = logger;
    }

    // ── IAddonManager ─────────────────────────────────────────────────────

    public IReadOnlyList<AddonInfo> GetInstalledAddons(string serverDir)
    {
        var addons = new List<AddonInfo>();

        CollectAddons(Path.Combine(serverDir, BehaviorPacksDir), "behavior", addons);
        CollectAddons(Path.Combine(serverDir, ResourcePacksDir), "resource", addons);

        return addons;
    }

    public async Task InstallAsync(string sourceFilePath, string serverDir, CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Addon file not found: {sourceFilePath}");

        string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is not ".mcpack" and not ".mcaddon" and not ".zip")
            throw new NotSupportedException($"Unsupported addon format: {ext}");

        string tempDir = Path.Combine(Path.GetTempPath(), $"bds-addon-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            _logger.LogInformation("Extracting addon {File} to temp dir {TempDir}.", sourceFilePath, tempDir);
            await SafeZipExtractor.ExtractAsync(sourceFilePath, tempDir);

            var manifests = FindManifests(tempDir);
            if (manifests.Count == 0)
                throw new InvalidOperationException(
                    $"No manifest.json found inside '{Path.GetFileName(sourceFilePath)}'. " +
                    "Is this a valid Bedrock add-on?");

            _logger.LogInformation("Found {Count} manifest(s) in addon.", manifests.Count);

            foreach (var manifestPath in manifests)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessManifestAsync(manifestPath, serverDir, ct);
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    public Task UninstallAsync(string addonPathOrId, string serverDir, CancellationToken ct = default)
    {
        // Uninstall = delete the pack directory from behavior_packs / resource_packs
        // and scrub the UUID from the world JSON files.
        string? packDir = null;
        string? packUuid = null;
        string? packType = null;

        foreach (var subDir in new[] { BehaviorPacksDir, ResourcePacksDir })
        {
            string candidate = Path.Combine(serverDir, subDir, addonPathOrId);
            if (Directory.Exists(candidate))
            {
                packDir  = candidate;
                packType = subDir == BehaviorPacksDir ? "data" : "resources";
                // Try to read UUID from the manifest so we can scrub the world JSON.
                var mPath = Path.Combine(candidate, "manifest.json");
                if (File.Exists(mPath))
                    packUuid = TryReadUuid(mPath);
                break;
            }
        }

        if (packDir == null)
            throw new DirectoryNotFoundException($"Pack directory not found for id '{addonPathOrId}'.");

        Directory.Delete(packDir, recursive: true);
        _logger.LogInformation("Deleted pack directory {Dir}.", packDir);

        if (packUuid != null)
        {
            string worldDir = ResolveWorldDirectory(serverDir);
            string jsonFile = packType == "data"
                ? Path.Combine(worldDir, WorldBehaviorJson)
                : Path.Combine(worldDir, WorldResourceJson);

            RemoveFromWorldJson(jsonFile, packUuid);
        }

        return Task.CompletedTask;
    }

    // ── Core installation logic ────────────────────────────────────────────

    private async Task ProcessManifestAsync(string manifestPath, string serverDir, CancellationToken ct)
    {
        ManifestInfo manifest;
        try
        {
            manifest = ParseManifest(manifestPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipping invalid manifest at {Path}.", manifestPath);
            return;
        }

        // The pack root dir is the directory that contains this manifest.json.
        string packSourceDir = Path.GetDirectoryName(manifestPath)!;
        string packName = SanitizeDirName(manifest.Name ?? manifest.Uuid);

        bool isBehavior = manifest.PackType == PackType.Data;
        string targetSubDir = isBehavior ? BehaviorPacksDir : ResourcePacksDir;
        string packDestDir  = Path.Combine(serverDir, targetSubDir, packName);

        _logger.LogInformation(
            "Installing {Type} pack '{Name}' (uuid={Uuid}) → {Dest}.",
            isBehavior ? "behavior" : "resource", packName, manifest.Uuid, packDestDir);

        // Overwrite any existing version of this pack.
        if (Directory.Exists(packDestDir)) Directory.Delete(packDestDir, recursive: true);
        await Task.Run(() => CopyDirectory(packSourceDir, packDestDir), ct);

        // Register in the active world json.
        string worldDir  = ResolveWorldDirectory(serverDir);
        string worldJson = isBehavior
            ? Path.Combine(worldDir, WorldBehaviorJson)
            : Path.Combine(worldDir, WorldResourceJson);

        RegisterInWorldJson(worldJson, manifest.Uuid, manifest.Version);
        _logger.LogInformation("Pack registered in {WorldJson}.", Path.GetFileName(worldJson));
    }

    // ── Manifest parsing ──────────────────────────────────────────────────

    private static ManifestInfo ParseManifest(string manifestPath)
    {
        string json = File.ReadAllText(manifestPath);
        var doc = JsonNode.Parse(json)
            ?? throw new JsonException("manifest.json is empty or not valid JSON.");

        string uuid = doc["header"]?["uuid"]?.GetValue<string>()
            ?? throw new KeyNotFoundException("manifest.json is missing header.uuid.");

        // Version is stored as an array e.g. [1, 0, 0]
        string version = "1.0.0";
        var verNode = doc["header"]?["version"];
        if (verNode is JsonArray arr && arr.Count >= 3)
            version = $"{arr[0]!.GetValue<int>()}.{arr[1]!.GetValue<int>()}.{arr[2]!.GetValue<int>()}";

        // Determine pack type from the modules array
        var moduleType = doc["modules"]?[0]?["type"]?.GetValue<string>() ?? "";
        var packType   = moduleType.Equals("resources", StringComparison.OrdinalIgnoreCase)
            ? PackType.Resources
            : PackType.Data;  // "data", "script", "world_template" → behavior packs

        string? name = doc["header"]?["name"]?.GetValue<string>();

        return new ManifestInfo(uuid, version, packType, name);
    }

    private static string? TryReadUuid(string manifestPath)
    {
        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(manifestPath));
            return doc?["header"]?["uuid"]?.GetValue<string>();
        }
        catch { return null; }
    }

    // ── World JSON management ─────────────────────────────────────────────

    private void RegisterInWorldJson(string jsonFilePath, string uuid, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonFilePath)!);

        JsonArray entries;
        if (File.Exists(jsonFilePath))
        {
            try
            {
                entries = JsonNode.Parse(File.ReadAllText(jsonFilePath)) as JsonArray ?? new JsonArray();
            }
            catch
            {
                entries = new JsonArray();
            }
        }
        else
        {
            entries = new JsonArray();
        }

        // Avoid duplicate entries — remove any existing entry with the same UUID.
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i]?["pack_id"]?.GetValue<string>() == uuid)
                entries.RemoveAt(i);
        }

        // Parse version string back to array for BDS JSON format.
        int[] verParts = ParseVersionParts(version);
        var newEntry = new JsonObject
        {
            ["pack_id"] = uuid,
            ["version"]  = new JsonArray(verParts[0], verParts[1], verParts[2])
        };
        entries.Add(newEntry);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(jsonFilePath, entries.ToJsonString(options));
    }

    private void RemoveFromWorldJson(string jsonFilePath, string uuid)
    {
        if (!File.Exists(jsonFilePath)) return;
        try
        {
            var entries = JsonNode.Parse(File.ReadAllText(jsonFilePath)) as JsonArray ?? new JsonArray();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i]?["pack_id"]?.GetValue<string>() == uuid)
                    entries.RemoveAt(i);
            }
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(jsonFilePath, entries.ToJsonString(options));
            _logger.LogInformation("Removed UUID {Uuid} from {File}.", uuid, Path.GetFileName(jsonFilePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrub UUID {Uuid} from {File}.", uuid, jsonFilePath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Recursively finds every manifest.json in <paramref name="root"/>.</summary>
    private static List<string> FindManifests(string root) =>
        Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories).ToList();

    private static string ResolveWorldDirectory(string serverDir)
    {
        // BDS stores worlds under [server]/worlds/<WorldName>/
        // Prefer the default directory but fall back to the first existing world dir.
        string preferred = Path.Combine(serverDir, WorldsDir, DefaultWorldName);
        if (Directory.Exists(preferred)) return preferred;

        var worldsParent = Path.Combine(serverDir, WorldsDir);
        if (Directory.Exists(worldsParent))
        {
            var first = Directory.GetDirectories(worldsParent).FirstOrDefault();
            if (first != null) return first;
        }

        // If no world directory exists yet, create the default one.
        Directory.CreateDirectory(preferred);
        return preferred;
    }

    private static void CollectAddons(string dir, string addonType, List<AddonInfo> output)
    {
        if (!Directory.Exists(dir)) return;

        // Each sub-directory is an installed pack.
        foreach (var packDir in Directory.GetDirectories(dir))
        {
            var manifestPath = Path.Combine(packDir, "manifest.json");
            string name = Path.GetFileName(packDir);
            if (File.Exists(manifestPath))
            {
                try
                {
                    var doc  = JsonNode.Parse(File.ReadAllText(manifestPath));
                    name = doc?["header"]?["name"]?.GetValue<string>() ?? name;
                }
                catch { /* use directory name */ }
            }

            long size = GetDirectorySizeBytes(packDir);
            output.Add(new AddonInfo
            {
                Name         = name,
                FilePath     = packDir,
                AddonType    = addonType,
                SizeKb       = size / 1024.0,
                LastModified = Directory.GetLastWriteTime(packDir)
            });
        }
    }

    private static long GetDirectorySizeBytes(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var subDir in Directory.GetDirectories(source))
            CopyDirectory(subDir, Path.Combine(dest, Path.GetFileName(subDir)));
    }

    private static string SanitizeDirName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    private static int[] ParseVersionParts(string version)
    {
        var parts = version.Split('.');
        int[] result = { 1, 0, 0 };
        for (int i = 0; i < Math.Min(3, parts.Length); i++)
            if (int.TryParse(parts[i], out int v)) result[i] = v;
        return result;
    }

    private void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp dir {Dir}.", dir); }
    }

    // ── Internal types ────────────────────────────────────────────────────

    private enum PackType { Data, Resources }

    private sealed record ManifestInfo(string Uuid, string Version, PackType PackType, string? Name);
}
