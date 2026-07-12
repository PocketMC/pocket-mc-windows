using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Application.Interfaces.Instances;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Domain.Models;

using PocketMC.Infrastructure.Instances;

using PocketMC.Infrastructure.Java;

namespace PocketMC.Infrastructure.Instances.Providers;

public class ForgeProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;

    public string DisplayName => "Forge";

    public ForgeProvider(HttpClient httpClient, DownloaderService downloader)
    {
        _httpClient = httpClient;
        _downloader = downloader;
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        const string metadataUrl = "https://meta.prismlauncher.org/v1/net.minecraftforge/index.json";
        var response = await _httpClient.GetFromJsonAsync<JsonObject>(metadataUrl);

        var mcToLoaders = new Dictionary<string, List<ModLoaderVersion>>();
        var minVersion = new Version(1, 8, 8);

        if (response != null && response.TryGetPropertyValue("versions", out var versionsNode) && versionsNode is JsonArray versionsArray)
        {
            foreach (var node in versionsArray)
            {
                if (node is JsonObject vObj)
                {
                    string? version = vObj["version"]?.ToString();
                    bool isRecommended = vObj["recommended"]?.GetValue<bool>() ?? false;

                    if (string.IsNullOrEmpty(version)) continue;

                    string mcVersion = "";
                    if (vObj.TryGetPropertyValue("requires", out var requiresNode) && requiresNode is JsonArray requiresArray)
                    {
                        foreach (var req in requiresArray)
                        {
                            if (req is JsonObject reqObj && reqObj["uid"]?.ToString() == "net.minecraft")
                            {
                                mcVersion = reqObj["equals"]?.ToString() ?? "";
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(mcVersion)) continue;
                    if (!JavaRuntimeResolver.TryParseVersion(mcVersion, out var parsedVersion) || parsedVersion < minVersion)
                        continue;

                    if (!mcToLoaders.ContainsKey(mcVersion))
                        mcToLoaders[mcVersion] = new List<ModLoaderVersion>();

                    mcToLoaders[mcVersion].Add(new ModLoaderVersion
                    {
                        Version = version,
                        IsStable = isRecommended || !version.Contains("-beta")
                    });
                }
            }
        }

        var result = new List<MinecraftVersion>();
        foreach (var kvp in mcToLoaders)
        {
            result.Add(new GameVersionWithLoaders
            {
                Id = kvp.Key,
                Type = "release",
                ReleaseTime = DateTime.MinValue,
                LoaderVersions = kvp.Value.OrderByDescending(l => l.IsStable).ThenByDescending(l => l.Version, new VersionStringComparer()).ToList()
            });
        }

        return result
            .OrderByDescending(v =>
            {
                var parts = v.Id.Split('.');
                long total = 0;
                for (int i = 0; i < Math.Min(parts.Length, 3); i++)
                {
                    if (long.TryParse(parts[i], out var p))
                        total += p * (long)Math.Pow(1000, 2 - i);
                }
                return total;
            })
            .ToList();
    }

    public async Task DownloadSoftwareAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string forgeVersion = await GetLatestForgeVersionAsync(mcVersion);
        await DownloadForgeJarAsync(mcVersion, forgeVersion, destinationPath, progress, cancellationToken);
    }

    public async Task DownloadForgeJarAsync(string mcVersion, string forgeVersion, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Build the download URL for the installer
        // Official: https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.20/forge-1.20.1-47.2.20-installer.jar
        string url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";

        // NOTE: Forge installers need to be RUN to generate the server. 
        // For now, we download the installer. The instance launch logic will need to handle the "installation" step.
        await _downloader.DownloadFileAsync(url, destinationPath, null, progress, cancellationToken);
    }

    private async Task<string> GetLatestForgeVersionAsync(string mcVersion)
    {
        var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
        if (response != null && response.TryGetPropertyValue("promos", out var promosNode) && promosNode is JsonObject promos)
        {
            // Format: "1.20.1-recommended": "47.2.0"
            if (promos.TryGetPropertyValue($"{mcVersion}-recommended", out var rec))
                return rec?.ToString() ?? "0";

            if (promos.TryGetPropertyValue($"{mcVersion}-latest", out var lat))
                return lat?.ToString() ?? "0";
        }

        return "latest";
    }
}

