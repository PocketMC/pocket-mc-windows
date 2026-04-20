using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Java;

namespace PocketMC.Desktop.Features.Instances.Providers;

public class PaperProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;

    public string DisplayName => "Paper (High Performance)";

    public PaperProvider(HttpClient httpClient, DownloaderService downloader)
    {
        _httpClient = httpClient;
        _downloader = downloader;
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        string json = await _httpClient.GetStringAsync("https://api.papermc.io/v2/projects/paper");
        var root = JsonNode.Parse(json);
        var versionsArray = root?["versions"]?.AsArray();

        var versions = new List<MinecraftVersion>();
        if (versionsArray != null)
        {
            var minVersion = new Version(1, 8, 8);
            // Paper provides an array of version strings, older to newer
            foreach (var v in versionsArray.Reverse())
            {
                if (v == null) continue;

                string vStr = v.ToString();
                if (!JavaRuntimeResolver.TryParseVersion(vStr, out var version) || version < minVersion)
                    continue;

                string type = "release";
                if (vStr.Contains("-") || System.Text.RegularExpressions.Regex.IsMatch(vStr, @"[a-zA-Z]"))
                    type = "snapshot";

                versions.Add(new MinecraftVersion
                {
                    Id = vStr,
                    Type = type,
                    ReleaseTime = DateTime.MinValue
                });
            }
        }

        return versions;
    }

    public async Task DownloadSoftwareAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
    {
        // Get latest build
        string versionJson = await _httpClient.GetStringAsync($"https://api.papermc.io/v2/projects/paper/versions/{mcVersion}");
        var root = JsonNode.Parse(versionJson);
        var buildsArray = root?["builds"]?.AsArray();

        if (buildsArray == null || buildsArray.Count == 0)
            throw new Exception($"No builds found for Paper version {mcVersion}.");

        // Assuming builds are in order, or we take the highest integer
        int maxBuild = buildsArray.Max(b => (int)b!);

        string jarName = $"paper-{mcVersion}-{maxBuild}.jar";
        string downloadUrl = $"https://api.papermc.io/v2/projects/paper/versions/{mcVersion}/builds/{maxBuild}/downloads/{jarName}";
        string? expectedSha256 = root?["downloads"]?["application"]?["sha256"]?.ToString();

        await _downloader.DownloadFileAsync(downloadUrl, destinationPath, expectedSha256, progress);
    }
}
