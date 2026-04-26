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
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Java;

namespace PocketMC.Desktop.Features.Instances.Providers;

public class VanillaProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationState _appState;
    private readonly DownloaderService _downloader;
    private readonly ILogger<VanillaProvider> _logger;

    public string DisplayName => "Vanilla (Official)";

    public VanillaProvider(
        HttpClient httpClient,
        ApplicationState appState,
        DownloaderService downloader,
        ILogger<VanillaProvider> logger)
    {
        _httpClient = httpClient;
        _appState = appState;
        _downloader = downloader;
        _logger = logger;
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        try
        {
            string url = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
            var response = await _httpClient.GetStringAsync(url);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var minVersion = new Version(1, 8, 8);
            return manifest?.Versions
                .Where(v => JavaRuntimeResolver.TryParseVersion(v.Id, out var version) && version >= minVersion)
                .ToList() ?? new List<MinecraftVersion>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Vanilla versions.");
            return new List<MinecraftVersion>();
        }
    }

    public async Task DownloadSoftwareAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving download URL for Vanilla {Version}", mcVersion);

        // 1. Get manifest to find version metadata URL
        string manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        var manifestStr = await _httpClient.GetStringAsync(manifestUrl);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var versionMeta = manifest?.Versions.FirstOrDefault(v => v.Id == mcVersion);
        if (versionMeta == null)
            throw new Exception($"Version {mcVersion} not found in Mojang manifest.");

        // 2. Get version specific metadata to find server jar URL
        var metaStr = await _httpClient.GetStringAsync(versionMeta.Url);
        var metaRoot = JsonNode.Parse(metaStr);
        var serverDownloadUrl = metaRoot?["downloads"]?["server"]?["url"]?.ToString();
        var serverSha1 = metaRoot?["downloads"]?["server"]?["sha1"]?.ToString();

        if (string.IsNullOrEmpty(serverDownloadUrl))
            throw new Exception($"No server download found for Vanilla {mcVersion}. Use a different provider or version.");

        // 3. Download
        await _downloader.DownloadFileAsync(serverDownloadUrl, destinationPath, serverSha1, progress, cancellationToken);
    }

    private class VersionManifest
    {
        public List<MinecraftVersion> Versions { get; set; } = new();
    }
}
