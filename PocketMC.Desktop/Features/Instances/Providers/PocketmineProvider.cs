using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.Instances.Providers;

public class PocketmineProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;
    private readonly ILogger<PocketmineProvider> _logger;

    public string DisplayName => "Pocketmine-MP (PHP)";

    public PocketmineProvider(HttpClient httpClient, DownloaderService downloader, ILogger<PocketmineProvider> logger)
    {
        _httpClient = httpClient;
        _downloader = downloader;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any(x => x.Product?.Name == "PocketMC.Desktop"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PocketMC.Desktop/1.3.0");
        }
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        var versions = new List<MinecraftVersion>();
        try
        {
            var response = await _httpClient.GetFromJsonAsync<JsonArray>("https://api.github.com/repos/pmmp/PocketMine-MP/releases");
            if (response != null)
            {
                foreach (var node in response)
                {
                    if (node is JsonObject releaseObj)
                    {
                        var tag = releaseObj["tag_name"]?.ToString() ?? "";
                        var isPreRelease = (bool)(releaseObj["prerelease"] ?? false);
                        
                        // Check if it has the PocketMine-MP.phar asset
                        var assets = releaseObj["assets"] as JsonArray;
                        if (assets != null && assets.Any(a => a is JsonObject aObj && aObj["name"]?.ToString() == "PocketMine-MP.phar"))
                        {
                            versions.Add(new MinecraftVersion
                            {
                                Id = tag,
                                Type = isPreRelease ? "snapshot" : "release",
                                ReleaseTime = DateTime.MinValue
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Pocketmine releases from GitHub.");
        }
        return versions;
    }

    public async Task DownloadSoftwareAsync(string versionId, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving download URL for Pocketmine {Version}", versionId);
        
        var response = await _httpClient.GetFromJsonAsync<JsonArray>("https://api.github.com/repos/pmmp/PocketMine-MP/releases");
        string? downloadUrl = null;
        
        if (response != null)
        {
            var release = response.FirstOrDefault(n => n is JsonObject r && r["tag_name"]?.ToString() == versionId) as JsonObject;
            if (release != null)
            {
                var assets = release["assets"] as JsonArray;
                if (assets != null)
                {
                    var pharAsset = assets.FirstOrDefault(a => a is JsonObject aObj && aObj["name"]?.ToString() == "PocketMine-MP.phar") as JsonObject;
                    downloadUrl = pharAsset?["browser_download_url"]?.ToString();
                }
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            throw new Exception($"Could not find a valid PocketMine-MP.phar download URL for version {versionId}.");
        }

        await _downloader.DownloadFileAsync(downloadUrl, destinationPath, null, progress, cancellationToken);
    }
}
