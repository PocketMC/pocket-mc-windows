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
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (serverType.StartsWith("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Geyser requires Paper, Fabric, or Forge. Vanilla is not supported.");
            }

            if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) ||
                serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Bedrock and PocketMine servers do not need Geyser.");
            }

            string loader = ResolveLoader(serverType, minecraftVersion);
            string targetDir = loader is "fabric" or "forge" or "neoforge" ? "mods" : "plugins";
            string dirPath = Path.Combine(instancePath, targetDir);
            Directory.CreateDirectory(dirPath);

            onProgress?.Invoke($"Checking Geyser compatibility for {serverType} {minecraftVersion}...");
            var geyserVersion = await _modrinth.GetLatestVersionAsync("geyser", minecraftVersion, loader);
            if (geyserVersion == null)
            {
                throw new InvalidOperationException(
                    $"Geyser does not currently support Minecraft {minecraftVersion} on {serverType}. " +
                    $"Check https://modrinth.com/mod/geyser for supported versions.");
            }

            var geyserFile = geyserVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? geyserVersion.Files.FirstOrDefault();
            if (geyserFile == null)
            {
                throw new InvalidOperationException("Modrinth returned a version with no downloadable files.");
            }

            string geyserPath = Path.Combine(dirPath, "Geyser.jar");
            onProgress?.Invoke($"Downloading Geyser ({loader})...");
            await _downloader.DownloadFileAsync(geyserFile.Url, geyserPath, null, progress, cancellationToken);

            onProgress?.Invoke("Checking Floodgate compatibility...");
            var floodgateVersion = await _modrinth.GetLatestVersionAsync("floodgate", minecraftVersion, loader);
            if (floodgateVersion == null)
            {
                _logger.LogWarning("Floodgate not found for {ServerType} {McVersion}. Installing Geyser only.", serverType, minecraftVersion);
                onProgress?.Invoke($"Warning: Floodgate not available for {serverType} {minecraftVersion}. Geyser only.");
                onProgress?.Invoke("Warning: Floodgate not available — Geyser only.");
                onProgress?.Invoke("Cross-play setup complete.");
                WriteConnectGuide(instancePath, targetDir);
                return;
            }

            var floodgateFile = floodgateVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? floodgateVersion.Files.FirstOrDefault();
            if (floodgateFile == null)
            {
                throw new InvalidOperationException("Modrinth returned a version with no downloadable files.");
            }

            string floodgatePath = Path.Combine(dirPath, "Floodgate.jar");
            onProgress?.Invoke($"Downloading Floodgate ({loader})...");
            await _downloader.DownloadFileAsync(floodgateFile.Url, floodgatePath, null, progress, cancellationToken);

            // Write a README so users know how to connect Bedrock clients
            WriteConnectGuide(instancePath, targetDir);
            onProgress?.Invoke("Cross-play setup complete.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Geyser and Floodgate for {ServerType}.", serverType);
            throw new InvalidOperationException(
                $"Could not set up cross-play for {serverType} {minecraftVersion}. " +
                "Please try again later or install Geyser manually from Modrinth.");
        }
    }

    private static string ResolveLoader(string serverType, string minecraftVersion)
    {
        if (serverType.Equals("Paper", StringComparison.OrdinalIgnoreCase) ||
            serverType.Equals("Spigot", StringComparison.OrdinalIgnoreCase))
        {
            return "spigot";
        }

        if (serverType.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return "fabric";
        }

        if (serverType.Equals("Forge", StringComparison.OrdinalIgnoreCase))
        {
            return IsAtLeastMinecraftVersion(minecraftVersion, 1, 20, 5) ? "neoforge" : "forge";
        }

        return "spigot";
    }

    private static bool IsAtLeastMinecraftVersion(string minecraftVersion, int major, int minor, int patch)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            return false;
        }

        var parts = minecraftVersion.Split('-', 2)[0].Split('.');
        int parsedMajor = parts.Length > 0 && int.TryParse(parts[0], out int maj) ? maj : 0;
        int parsedMinor = parts.Length > 1 && int.TryParse(parts[1], out int min) ? min : 0;
        int parsedPatch = parts.Length > 2 && int.TryParse(parts[2], out int pat) ? pat : 0;

        if (parsedMajor != major) return parsedMajor > major;
        if (parsedMinor != minor) return parsedMinor > minor;
        return parsedPatch >= patch;
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
