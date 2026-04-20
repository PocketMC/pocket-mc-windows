using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Java;

namespace PocketMC.Desktop.Features.Instances.Providers;

public class FabricProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;

    public string DisplayName => "Fabric";

    public FabricProvider(HttpClient httpClient, DownloaderService downloader)
    {
        _httpClient = httpClient;
        _downloader = downloader;
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        var gameVersionsResponse = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/game");
        var loadersResponse = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/loader");

        var loaders = new List<ModLoaderVersion>();
        if (loadersResponse != null)
        {
            foreach (var node in loadersResponse)
            {
                if (node == null) continue;
                loaders.Add(new ModLoaderVersion
                {
                    Version = node["version"]?.ToString() ?? "",
                    IsStable = (bool)(node["stable"] ?? false)
                });
            }
        }

        var versions = new List<MinecraftVersion>();
        if (gameVersionsResponse != null)
        {
            var minVersion = new Version(1, 14, 0);
            foreach (var node in gameVersionsResponse)
            {
                if (node == null) continue;
                string vStr = node["version"]?.ToString() ?? "";
                if (!JavaRuntimeResolver.TryParseVersion(vStr, out var version) || version < minVersion)
                    continue;

                versions.Add(new GameVersionWithLoaders
                {
                    Id = vStr,
                    Type = (bool)(node["stable"] ?? false) ? "release" : "snapshot",
                    ReleaseTime = DateTime.MinValue,
                    LoaderVersions = loaders // In Fabric, generally any recent loader works with any game version
                });
            }
        }
        return versions;
    }

    public async Task DownloadSoftwareAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
    {
        // Get latest loader and installer versions
        string loaderVersion = await GetLatestLoaderVersionAsync();
        string installerVersion = await GetLatestInstallerVersionAsync();
        await DownloadFabricJarAsync(mcVersion, loaderVersion, installerVersion, destinationPath, progress);
    }

    public async Task DownloadFabricJarAsync(string mcVersion, string loaderVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
    {
        string installerVersion = await GetLatestInstallerVersionAsync();
        await DownloadFabricJarAsync(mcVersion, loaderVersion, installerVersion, destinationPath, progress);
    }

    public async Task DownloadFabricJarAsync(string mcVersion, string loaderVersion, string installerVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
    {
        // Official structure: v2/versions/loader/:game_version/:loader_version/:installer_version/server/jar
        string url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/{installerVersion}/server/jar";
        await _downloader.DownloadFileAsync(url, destinationPath, null, progress);
    }

    private async Task<string> GetLatestLoaderVersionAsync()
    {
        var loaders = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/loader");
        var latest = loaders?.FirstOrDefault(l => (bool)(l?["stable"] ?? false));
        return latest?["version"]?.ToString() ?? "0.15.7";
    }

    private async Task<string> GetLatestInstallerVersionAsync()
    {
        var installers = await _httpClient.GetFromJsonAsync<JsonArray>("https://meta.fabricmc.net/v2/versions/installer");
        var latest = installers?.FirstOrDefault(l => (bool)(l?["stable"] ?? false));
        return latest?["version"]?.ToString() ?? "1.0.1";
    }
}
