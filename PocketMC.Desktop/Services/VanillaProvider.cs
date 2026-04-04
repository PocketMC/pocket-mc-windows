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

namespace PocketMC.Desktop.Services
{
    public class VanillaProvider : IServerJarProvider
    {
        private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
        private readonly HttpClient _httpClient;
        private readonly ApplicationState _applicationState;
        private readonly DownloaderService _downloader;
        private readonly ILogger<VanillaProvider> _logger;

        public string DisplayName => "Vanilla (Mojang)";

        public VanillaProvider(
            HttpClient httpClient,
            ApplicationState applicationState,
            DownloaderService downloader,
            ILogger<VanillaProvider> logger)
        {
            _httpClient = httpClient;
            _applicationState = applicationState;
            _downloader = downloader;
            _logger = logger;
        }

        public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            string manifestJson = await GetManifestJsonAsync(allowStaleCacheFallback: true);

            var root = JsonNode.Parse(manifestJson);
            var versionsArray = root?["versions"]?.AsArray();

            var versions = new List<MinecraftVersion>();
            if (versionsArray != null)
            {
                foreach (var v in versionsArray)
                {
                    if (v == null) continue;
                    
                    versions.Add(new MinecraftVersion
                    {
                        Id = v["id"]?.ToString() ?? "",
                        Type = v["type"]?.ToString() ?? "",
                        ReleaseTime = DateTime.TryParse(v["releaseTime"]?.ToString(), out var dt) ? dt : DateTime.MinValue
                    });
                }
            }

            return versions;
        }

        public async Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            string manifestJson = await GetManifestJsonAsync(allowStaleCacheFallback: true);

            var root = JsonNode.Parse(manifestJson);
            var versionsArray = root?["versions"]?.AsArray();
            var targetVersionNode = versionsArray?.FirstOrDefault(v => v?["id"]?.ToString() == mcVersion);

            if (targetVersionNode == null)
                throw new Exception($"Version {mcVersion} not found in Mojang manifest.");

            string versionUrl = targetVersionNode["url"]?.ToString() ?? "";
            string versionJson = await _httpClient.GetStringAsync(versionUrl);
            
            var versionRoot = JsonNode.Parse(versionJson);
            string serverUrl = versionRoot?["downloads"]?["server"]?["url"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(serverUrl))
                throw new Exception($"Vanilla server jar not available for {mcVersion}.");

            await _downloader.DownloadFileAsync(serverUrl, destinationPath, progress);
        }

        private async Task<string> GetManifestJsonAsync(bool allowStaleCacheFallback)
        {
            string? cachePath = GetCachePath();
            if (TryReadFreshCache(cachePath, out var cachedManifest))
            {
                return cachedManifest!;
            }

            try
            {
                string manifestJson = await _httpClient.GetStringAsync(ManifestUrl);
                ValidateManifest(manifestJson);

                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllTextAsync(cachePath, manifestJson);
                return manifestJson;
            }
            catch (Exception ex) when (allowStaleCacheFallback)
            {
                if (TryReadAnyCache(cachePath, out var staleManifest))
                {
                    _logger.LogWarning(ex, "Falling back to a stale Mojang manifest cache at {CachePath}.", cachePath);
                    return staleManifest!;
                }

                throw;
            }
        }

        private bool TryReadFreshCache(string cachePath, out string? manifestJson)
        {
            manifestJson = null;
            if (!File.Exists(cachePath) ||
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalHours >= 1)
            {
                return false;
            }

            return TryReadCachedManifest(cachePath, out manifestJson);
        }

        private bool TryReadAnyCache(string cachePath, out string? manifestJson)
        {
            manifestJson = null;
            if (!File.Exists(cachePath))
            {
                return false;
            }

            return TryReadCachedManifest(cachePath, out manifestJson);
        }

        private bool TryReadCachedManifest(string cachePath, out string? manifestJson)
        {
            manifestJson = null;

            try
            {
                manifestJson = File.ReadAllText(cachePath);
                ValidateManifest(manifestJson);
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                _logger.LogWarning(ex, "Discarding invalid Mojang manifest cache at {CachePath}.", cachePath);
                TryDeleteCorruptCache(cachePath);
                manifestJson = null;
                return false;
            }
        }

        private static void ValidateManifest(string manifestJson)
        {
            var root = JsonNode.Parse(manifestJson);
            if (root?["versions"]?.AsArray() == null)
            {
                throw new InvalidDataException("Manifest JSON did not contain a valid versions array.");
            }
        }

        private static void TryDeleteCorruptCache(string cachePath)
        {
            try
            {
                File.Delete(cachePath);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        private string GetCachePath()
        {
            return Path.Combine(_applicationState.GetRequiredAppRootPath(), "manifest-cache.json");
        }
    }
}
