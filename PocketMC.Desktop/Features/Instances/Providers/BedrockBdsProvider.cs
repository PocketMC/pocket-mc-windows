using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances.Providers;

/// <summary>
/// Downloads Bedrock Dedicated Server (BDS) using the kittizz community JSON manifest
/// as a primary source — which is actively maintained, versioned, and doesn't require
/// scraping a JS-rendered page.
///
/// Primary:  https://raw.githubusercontent.com/kittizz/bedrock-server-downloads/main/bedrock-server-downloads.json
/// Fallback: EndstoneMC bedrock-server-data (GitHub API → raw asset)
///
/// JSON shape (kittizz):
/// <code>
/// {
///   "release": {
///     "1.21.90": { "windows": { "url": "https://..." }, "linux": { ... } },
///     ...
///   },
///   "preview": { ... }
/// }
/// </code>
/// </summary>
public class BedrockBdsProvider : IServerSoftwareProvider
{
    private const string KittizzJsonUrl =
        "https://raw.githubusercontent.com/kittizz/bedrock-server-downloads/main/bedrock-server-downloads.json";

    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;
    private readonly ILogger<BedrockBdsProvider> _logger;

    // In-memory cache of (versionId → windowsDownloadUrl) built the first time versions are fetched.
    private Dictionary<string, string>? _releaseCache;
    private Dictionary<string, string>? _previewCache;

    public string DisplayName => "Bedrock (BDS)";

    public BedrockBdsProvider(
        HttpClient httpClient,
        DownloaderService downloader,
        ILogger<BedrockBdsProvider> logger)
    {
        _httpClient = httpClient;
        _downloader = downloader;
        _logger = logger;
    }

    // ── IServerSoftwareProvider ────────────────────────────────────────────

    /// <summary>
    /// Returns the full list of known stable BDS releases (and previews) pulled
    /// from the kittizz manifest, sorted newest-first by semantic version.
    /// </summary>
    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        await EnsureCacheAsync();

        var versions = new List<MinecraftVersion>();

        if (_releaseCache != null)
        {
            foreach (var (ver, _) in _releaseCache.OrderByDescending(kvp => kvp.Key, VersionComparer.Instance))
            {
                versions.Add(new MinecraftVersion { Id = ver, Type = "release", ReleaseTime = DateTime.UtcNow.Date });
            }
        }

        if (_previewCache != null)
        {
            foreach (var (ver, _) in _previewCache.OrderByDescending(kvp => kvp.Key, VersionComparer.Instance))
            {
                versions.Add(new MinecraftVersion { Id = ver, Type = "snapshot", ReleaseTime = DateTime.UtcNow.Date });
            }
        }

        if (versions.Count == 0)
        {
            // Absolute last resort — guarantees the UI is never empty.
            _logger.LogWarning("BDS manifest could not be fetched. Providing 'latest' fallback.");
            versions.Add(new MinecraftVersion { Id = "latest", Type = "release", ReleaseTime = DateTime.UtcNow.Date });
        }

        return versions;
    }

    /// <summary>
    /// Downloads the BDS ZIP to <paramref name="destinationPath"/> (a file path, e.g. C:\Temp\bds.zip).
    /// Extraction is performed by the caller (NewInstancePage via DownloaderService.ExtractZipAsync).
    /// </summary>
    public async Task DownloadSoftwareAsync(
        string versionId,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Preparing BDS download for version {Version}.", versionId);
        await EnsureCacheAsync();

        // Resolve URL: prefer exact version from cache, fall back to latest release.
        string? url = null;

        if (!string.IsNullOrWhiteSpace(versionId) && versionId != "latest")
        {
            _releaseCache?.TryGetValue(versionId, out url);
            if (url == null) _previewCache?.TryGetValue(versionId, out url);
        }

        if (url == null && _releaseCache?.Count > 0)
        {
            var latest = _releaseCache
                .OrderByDescending(kvp => kvp.Key, VersionComparer.Instance)
                .FirstOrDefault();
            url = latest.Value;
            _logger.LogInformation("Specific version not cached — using latest: {Version}", latest.Key);
        }

        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException(
                $"Could not resolve a download URL for Bedrock Dedicated Server {versionId}. " +
                "The kittizz manifest may be temporarily unreachable. Check your internet connection and try again.");
        }

        // Ensure parent directory exists (caller may pass a path like C:\Temp\bds-guid.zip)
        string? dir = System.IO.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        _logger.LogInformation("Downloading BDS {Version} from {Url}", versionId, url);
        await _downloader.DownloadFileAsync(url, destinationPath, null, progress, cancellationToken);
        _logger.LogInformation("BDS ZIP written to {Path}.", destinationPath);
    }


    // ── Cache ──────────────────────────────────────────────────────────────

    private async Task EnsureCacheAsync()
    {
        if (_releaseCache != null) return; // already populated

        try
        {
            using var response = await _httpClient.GetAsync(KittizzJsonUrl);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            ParseKittizzJson(json);
            _logger.LogInformation("BDS manifest loaded: {Count} releases, {PCount} previews.",
                _releaseCache?.Count ?? 0, _previewCache?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch BDS manifest from kittizz. Version list will be empty.");
            _releaseCache = new Dictionary<string, string>();
            _previewCache = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Parses the kittizz JSON format:
    /// <c>{ "release": { "1.21.90": { "windows": { "url": "..." } } }, "preview": { ... } }</c>
    /// </summary>
    private void ParseKittizzJson(string json)
    {
        _releaseCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _previewCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var root = JsonNode.Parse(json);
        if (root == null) return;

        FillDict(root["release"], _releaseCache);
        FillDict(root["preview"], _previewCache);
    }

    private static void FillDict(JsonNode? section, Dictionary<string, string> dict)
    {
        if (section is not JsonObject obj) return;

        foreach (var (ver, entry) in obj)
        {
            var winUrl = entry?["windows"]?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(winUrl))
                dict[ver] = winUrl;
        }
    }

    // ── Version comparer (semantic sort for BDS versions like "1.21.90") ──

    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            static int[] Parts(string? s) =>
                (s ?? "0").Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();

            var px = Parts(x);
            var py = Parts(y);
            int len = Math.Max(px.Length, py.Length);

            for (int i = 0; i < len; i++)
            {
                int a = i < px.Length ? px[i] : 0;
                int b = i < py.Length ? py[i] : 0;
                if (a != b) return a.CompareTo(b);
            }
            return 0;
        }
    }
}
