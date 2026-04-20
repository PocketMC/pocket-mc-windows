using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.Instances.Providers;

public class PocketmineProvider : IServerSoftwareProvider
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/pmmp/PocketMine-MP/releases";
    private static readonly TimeSpan ReleaseCacheLifetime = TimeSpan.FromMinutes(10);
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;
    private readonly ILogger<PocketmineProvider> _logger;
    private readonly SemaphoreSlim _releaseCacheLock = new(1, 1);
    private JsonArray? _cachedReleases;
    private DateTimeOffset _cachedReleasesFetchedAtUtc = DateTimeOffset.MinValue;

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
            JsonArray response = await GetReleasesAsync();
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Pocketmine release listing is temporarily unavailable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Pocketmine releases from GitHub.");
        }
        return versions;
    }

    public async Task DownloadSoftwareAsync(string versionId, string destinationPath, IProgress<DownloadProgress>? progress = null)
    {
        _logger.LogInformation("Resolving download URL for Pocketmine {Version}", versionId);

        JsonArray response = await GetReleasesAsync();
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

        await _downloader.DownloadFileAsync(downloadUrl, destinationPath, null, progress);
    }

    private async Task<JsonArray> GetReleasesAsync()
    {
        bool hasFreshCache = _cachedReleases != null &&
                             DateTimeOffset.UtcNow - _cachedReleasesFetchedAtUtc <= ReleaseCacheLifetime;
        if (hasFreshCache)
        {
            return _cachedReleases!;
        }

        await _releaseCacheLock.WaitAsync();
        try
        {
            hasFreshCache = _cachedReleases != null &&
                            DateTimeOffset.UtcNow - _cachedReleasesFetchedAtUtc <= ReleaseCacheLifetime;
            if (hasFreshCache)
            {
                return _cachedReleases!;
            }

            using HttpRequestMessage request = new(HttpMethod.Get, ReleasesApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                throw CreateRateLimitException(response);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"GitHub release API returned {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    inner: null,
                    response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            JsonNode? payload = await JsonNode.ParseAsync(stream);
            if (payload is not JsonArray releases)
            {
                throw new InvalidOperationException("GitHub release response was not in the expected format.");
            }

            _cachedReleases = releases;
            _cachedReleasesFetchedAtUtc = DateTimeOffset.UtcNow;
            return releases;
        }
        finally
        {
            _releaseCacheLock.Release();
        }
    }

    private static InvalidOperationException CreateRateLimitException(HttpResponseMessage response)
    {
        string message = "GitHub API rate limit reached while fetching PocketMine releases. Please try again later.";

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out long resetEpochSeconds))
        {
            DateTimeOffset resetAt = DateTimeOffset.FromUnixTimeSeconds(resetEpochSeconds).ToLocalTime();
            message = $"GitHub API rate limit reached while fetching PocketMine releases. Try again after {resetAt:yyyy-MM-dd HH:mm zzz}.";
        }
        else if (response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
        {
            message = $"GitHub API rate limit reached while fetching PocketMine releases. Try again after {retryAt.ToLocalTime():yyyy-MM-dd HH:mm zzz}.";
        }
        else if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
        {
            message = $"GitHub API rate limit reached while fetching PocketMine releases. Try again in about {Math.Ceiling(retryAfter.TotalMinutes)} minute(s).";
        }

        return new InvalidOperationException(message);
    }
}
