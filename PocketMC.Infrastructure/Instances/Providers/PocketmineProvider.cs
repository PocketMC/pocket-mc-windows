using PocketMC.Application.Interfaces.Instances;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Infrastructure.Mods;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace PocketMC.Infrastructure.Instances.Providers;

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
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"PocketMC.Desktop/{AppConfig.AppVersion}");
        }
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        var versions = new List<MinecraftVersion>();
        try
        {
            string releasesUrl = $"{PocketMC.Infrastructure.Configuration.AppConfig.ProviderPocketmineReleases}?per_page=100";
            var response = await _httpClient.GetFromJsonAsync<JsonArray>(releasesUrl);
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

    public async Task<string> DownloadSoftwareAsync(string versionId, string destinationPath, string? loaderVersion = null, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resolving download URL for Pocketmine {Version}", versionId);

        string? downloadUrl = null;

        // Try direct tag release endpoint first for instant resolution
        try
        {
            string baseReleases = PocketMC.Infrastructure.Configuration.AppConfig.ProviderPocketmineReleases;
            var releaseObj = await _httpClient.GetFromJsonAsync<JsonObject>($"{baseReleases}/tags/{versionId}", cancellationToken);
            var assets = releaseObj?["assets"] as JsonArray;
            if (assets != null)
            {
                var pharAsset = assets.FirstOrDefault(a => a is JsonObject aObj && aObj["name"]?.ToString() == "PocketMine-MP.phar") as JsonObject;
                downloadUrl = pharAsset?["browser_download_url"]?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direct tag lookup failed for PocketMine {Version}, falling back to list lookup.", versionId);
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            string baseReleases = PocketMC.Infrastructure.Configuration.AppConfig.ProviderPocketmineReleases;
            var response = await _httpClient.GetFromJsonAsync<JsonArray>($"{baseReleases}?per_page=100", cancellationToken);
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
        }

        // Direct GitHub asset download fallback
        if (string.IsNullOrEmpty(downloadUrl))
        {
            string baseReleases = PocketMC.Infrastructure.Configuration.AppConfig.ProviderPocketmineReleases;
            string directDownloadBase = baseReleases.Replace("api.github.com/repos", "github.com");
            downloadUrl = $"{directDownloadBase}/download/{versionId}/PocketMine-MP.phar";
        }

        await _downloader.DownloadFileAsync(downloadUrl, destinationPath, null, progress, cancellationToken);
        
        return string.Empty;
    }
}

