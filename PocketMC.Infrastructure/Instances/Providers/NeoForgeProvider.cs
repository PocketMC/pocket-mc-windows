using PocketMC.Infrastructure.Instances.Providers;
using PocketMC.Application.Interfaces.Instances;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PocketMC.Domain.Models;

using PocketMC.Infrastructure.Instances;

namespace PocketMC.Infrastructure.Instances.Providers;

public class NeoForgeProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;

    public string DisplayName => "NeoForge";

    public NeoForgeProvider(HttpClient httpClient, DownloaderService downloader)
    {
        _httpClient = httpClient;
        _downloader = downloader;
    }

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
    {
        const string metadataUrl = "https://meta.prismlauncher.org/v1/net.neoforged/index.json";
        var response = await _httpClient.GetFromJsonAsync<JsonObject>(metadataUrl);

        var mcToLoaders = new Dictionary<string, List<ModLoaderVersion>>();

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
        // This is a fallback if someone calls DownloadSoftwareAsync directly without a loader version.
        // We'll try to find the latest stable version for the MC version.
        var versions = await GetAvailableVersionsAsync();
        var mcv = versions.FirstOrDefault(v => v.Id == mcVersion) as GameVersionWithLoaders;
        var latest = mcv?.LoaderVersions.FirstOrDefault();

        if (latest == null)
            throw new Exception($"No NeoForge versions found for Minecraft {mcVersion}");

        await DownloadNeoForgeJarAsync(mcVersion, latest.Version, destinationPath, progress, cancellationToken);
    }

    public async Task DownloadNeoForgeJarAsync(string mcVersion, string neoforgeVersion, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // URL Pattern: https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.65/neoforge-21.1.65-installer.jar
        string url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoforgeVersion}/neoforge-{neoforgeVersion}-installer.jar";
        await _downloader.DownloadFileAsync(url, destinationPath, null, progress, cancellationToken);
    }
}

