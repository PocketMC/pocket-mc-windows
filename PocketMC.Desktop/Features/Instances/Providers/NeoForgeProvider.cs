using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;

namespace PocketMC.Desktop.Features.Instances.Providers;

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
        const string metadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
        var response = await _httpClient.GetStringAsync(metadataUrl);
        var doc = XDocument.Parse(response);

        var versions = doc.Descendants("version")
            .Select(v => v.Value)
            .ToList();

        var mcToLoaders = new Dictionary<string, List<ModLoaderVersion>>();

        foreach (var v in versions)
        {
            // NeoForge versioning: XX.YY.ZZ maps to MC 1.XX.YY
            // Examples: 20.4.127 -> 1.20.4, 21.1.65 -> 1.21.1
            var parts = v.Split('.');
            if (parts.Length < 3) continue;

            if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
                continue;

            string mcVersion = $"1.{major}.{minor}";
            bool isBeta = v.Contains("-beta");

            if (!mcToLoaders.ContainsKey(mcVersion))
                mcToLoaders[mcVersion] = new List<ModLoaderVersion>();

            mcToLoaders[mcVersion].Add(new ModLoaderVersion
            {
                Version = v,
                IsStable = !isBeta
            });
        }

        var result = new List<MinecraftVersion>();
        foreach (var kvp in mcToLoaders)
        {
            result.Add(new GameVersionWithLoaders
            {
                Id = kvp.Key,
                Type = "release",
                ReleaseTime = DateTime.MinValue,
                LoaderVersions = kvp.Value.OrderByDescending(l => l.IsStable).ThenByDescending(l => l.Version).ToList()
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
