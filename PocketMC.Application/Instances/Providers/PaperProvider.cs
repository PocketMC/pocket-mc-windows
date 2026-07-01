using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Java;

namespace PocketMC.Application.Instances.Providers;

public class PaperProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;

    public string DisplayName => "Paper (High Performance)";

    public PaperProvider(HttpClient httpClient, DownloaderService downloader)
    {
        _httpClient = httpClient;
        _downloader = downloader;

        // Ensure proper User-Agent header for PaperMC Fill v3 API compliance
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PocketMC-Desktop/1.0 (https://github.com/pocketmc/pocket-mc-windows)");
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        string json = await _httpClient.GetStringAsync("https://fill.papermc.io/v3/projects/paper");
        var root = JsonNode.Parse(json);
        var versionsObject = root?["versions"]?.AsObject();

        var versions = new List<MinecraftVersion>();
        if (versionsObject != null)
        {
            var minVersion = new Version(1, 8, 8);
            var parsedVersions = new List<(string vStr, Version version)>();

            foreach (var property in versionsObject)
            {
                var array = property.Value?.AsArray();
                if (array == null) continue;

                foreach (var v in array)
                {
                    if (v == null) continue;
                    string vStr = v.ToString();
                    if (JavaRuntimeResolver.TryParseVersion(vStr, out var version) && version >= minVersion)
                    {
                        parsedVersions.Add((vStr, version));
                    }
                }
            }

            // Sort versions descending: first by version components, then releases before snapshots, then string descending
            var sorted = parsedVersions
                .OrderByDescending(x => x.version)
                .ThenBy(x => x.vStr.Contains("-") || System.Text.RegularExpressions.Regex.IsMatch(x.vStr, @"[a-zA-Z]"))
                .ThenByDescending(x => x.vStr)
                .ToList();

            foreach (var item in sorted)
            {
                string type = "release";
                if (item.vStr.Contains("-") || System.Text.RegularExpressions.Regex.IsMatch(item.vStr, @"[a-zA-Z]"))
                    type = "snapshot";

                versions.Add(new MinecraftVersion
                {
                    Id = item.vStr,
                    Type = type,
                    ReleaseTime = DateTime.MinValue
                });
            }
        }

        return versions;
    }

    public async Task DownloadSoftwareAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Get latest build using the v3 API
        string versionJson = await _httpClient.GetStringAsync($"https://fill.papermc.io/v3/projects/paper/versions/{mcVersion}");
        var root = JsonNode.Parse(versionJson);
        var buildsArray = root?["builds"]?.AsArray();

        if (buildsArray == null || buildsArray.Count == 0)
            throw new Exception($"No builds found for Paper version {mcVersion}.");

        // Take the highest integer build number
        int maxBuild = buildsArray.Max(b => (int)b!);

        // Fetch the detailed build information to get download URL and SHA256 checksum
        string buildJson = await _httpClient.GetStringAsync($"https://fill.papermc.io/v3/projects/paper/versions/{mcVersion}/builds/{maxBuild}");
        var buildRoot = JsonNode.Parse(buildJson);

        var downloadNode = buildRoot?["downloads"]?["server:default"] ?? buildRoot?["downloads"]?["application"];
        if (downloadNode == null)
            throw new Exception($"No download details found for Paper version {mcVersion} build {maxBuild}.");

        string? downloadUrl = downloadNode["url"]?.ToString();
        string? expectedSha256 = downloadNode["checksums"]?["sha256"]?.ToString() ?? downloadNode["sha256"]?.ToString();

        if (string.IsNullOrEmpty(downloadUrl))
            throw new Exception($"No download URL found for Paper version {mcVersion} build {maxBuild}.");

        await _downloader.DownloadFileAsync(downloadUrl, destinationPath, expectedSha256, progress, cancellationToken);
    }
}

