using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Instances.Models;

namespace PocketMC.Desktop.Features.Instances.Services;

public class GeyserProvisioningService
{
    private readonly DownloaderService _downloader;
    private readonly ModrinthService _modrinth;
    private readonly ILogger<GeyserProvisioningService> _logger;

    public GeyserProvisioningService(
        DownloaderService downloader,
        ModrinthService modrinth,
        ILogger<GeyserProvisioningService> logger)
    {
        _downloader = downloader;
        _modrinth = modrinth;
        _logger = logger;
    }

    /// <summary>
    /// Provisions Geyser and Floodgate for a given Java server instance.
    /// Deliberately does NOT pre-write config.yml — Geyser auto-generates a correct
    /// one on first run. A hand-crafted config risks schema mismatches with the
    /// installed Geyser build and will break plugin startup.
    /// 
    /// Connection info after first server run:
    ///   - Bedrock clients connect on the SAME IP as Java, port 19132 (UDP)
    ///   - Config lives in: plugins/Geyser-Spigot/config.yml  
    /// </summary>
    public async Task EnsureGeyserSetupAsync(
        string instancePath,
        string serverType,
        string minecraftVersion,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string platform = serverType.ToLowerInvariant() switch
            {
                "paper" or "spigot" => "spigot",  // Geyser calls it "spigot" for both Paper + Spigot
                "fabric"            => "fabric",
                "forge"             => "fabric",   // Forge uses the Fabric/NeoForge variant
                _                   => "spigot"
            };

            string targetDir = platform == "fabric" ? "mods" : "plugins";
            string dirPath = Path.Combine(instancePath, targetDir);
            Directory.CreateDirectory(dirPath);

            string geyserUrl = $"https://download.geysermc.org/v2/projects/geyser/versions/latest/builds/latest/downloads/{platform}";
            string? floodgateUrl = null;

            if (platform == "fabric")
            {
                // GeyserMC Build API has removed Fabric versions of Floodgate (moved to Modrinth).
                _logger.LogInformation("Fetching latest Floodgate ({Platform}) from Modrinth for MC {MinecraftVersion}...", platform, minecraftVersion);
                var version = await _modrinth.GetLatestVersionAsync("floodgate", minecraftVersion, "fabric");
                floodgateUrl = version?.Files.FirstOrDefault(f => f.IsPrimary)?.Url ?? version?.Files.FirstOrDefault()?.Url;

                if (string.IsNullOrEmpty(floodgateUrl))
                {
                    _logger.LogWarning("Could not find Floodgate for Fabric on Modrinth. Falling back to GeyserMC API (likely to fail).");
                    floodgateUrl = $"https://download.geysermc.org/v2/projects/floodgate/versions/latest/builds/latest/downloads/{platform}";
                }
            }
            else
            {
                floodgateUrl = $"https://download.geysermc.org/v2/projects/floodgate/versions/latest/builds/latest/downloads/{platform}";
            }

            string geyserPath    = Path.Combine(dirPath, "Geyser.jar");
            string floodgatePath = Path.Combine(dirPath, "Floodgate.jar");

            _logger.LogInformation("Downloading Geyser ({Platform})...", platform);
            await _downloader.DownloadFileAsync(geyserUrl, geyserPath, null, progress, cancellationToken);

            _logger.LogInformation("Downloading Floodgate from {Url}...", floodgateUrl);
            await _downloader.DownloadFileAsync(floodgateUrl, floodgatePath, null, progress, cancellationToken);

            // Write a README so users know how to connect Bedrock clients
            WriteConnectGuide(instancePath, targetDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Geyser and Floodgate for {ServerType}.", serverType);
            throw;
        }
    }

    private void WriteConnectGuide(string instancePath, string targetDir)
    {
        try
        {
            string guidePath = Path.Combine(instancePath, "BEDROCK-CONNECT.txt");
            if (File.Exists(guidePath)) return;

            File.WriteAllText(guidePath,
                "=== Bedrock Cross-Play (Geyser + Floodgate) ===\n\n" +
                "Java players:   Connect with the Java IP on port 25565 (as usual).\n" +
                "Bedrock players: Connect with the SAME IP on port 19132 (UDP).\n\n" +
                "First run:\n" +
                "  1. Start the server once — Geyser will auto-generate its config.yml\n" +
                $"     inside {targetDir}/Geyser-Spigot/config.yml\n" +
                "  2. Restart the server. Geyser will then listen on port 19132.\n\n" +
                "Tunneling (Playit.gg):\n" +
                "  - For your Java port tunnel, select: Minecraft Java\n" +
                "  - For your Bedrock port tunnel (19132), select: Minecraft Bedrock\n" +
                "  Both tunnels are needed for full cross-play.\n");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write Bedrock connect guide.");
        }
    }
}
