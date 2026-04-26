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
        // Fetch the official Forge versions JSON (slim promotions)
        var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
        var versions = new List<MinecraftVersion>();

        if (response != null && response.TryGetPropertyValue("promos", out var promosNode) && promosNode is JsonObject promos)
        {
            // We extract unique MC versions from the keys
            // Format of keys: "1.20.1-recommended", "1.20.1-latest"
            var mcToLoaders = new Dictionary<string, List<ModLoaderVersion>>();

            var minVersion = new Version(1, 8, 8);
            foreach (var entry in promos)
            {
                var parts = entry.Key.Split('-');
                if (parts.Length < 2) continue;

                string mcVersion = parts[0];
                if (!mcVersion.StartsWith("1.")) continue;

                if (!JavaRuntimeResolver.TryParseVersion(mcVersion, out var version) || version < minVersion)
                    continue;

                string promoType = parts[1];
                string forgeVersion = entry.Value?.ToString() ?? "";

                if (!mcToLoaders.ContainsKey(mcVersion))
                    mcToLoaders[mcVersion] = new List<ModLoaderVersion>();

                // Add this version if not already present
                if (!mcToLoaders[mcVersion].Any(l => l.Version == forgeVersion))
                {
                    mcToLoaders[mcVersion].Add(new ModLoaderVersion
                    {
                        Version = forgeVersion,
                        IsStable = promoType == "recommended"
                    });
                }
            }

            foreach (var kvp in mcToLoaders)
            {
                versions.Add(new GameVersionWithLoaders
                {
                    Id = kvp.Key,
                    Type = "release",
                    ReleaseTime = DateTime.MinValue,
                    LoaderVersions = kvp.Value.OrderByDescending(l => l.IsStable).ThenByDescending(l => l.Version).ToList()
                });
            }
        }

        // Numerical sort by MC version segments
        return versions
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
