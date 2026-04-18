using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Marketplace;

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
            string targetDir = ResolveTargetDirectory(loader);
            string dirPath = Path.Combine(instancePath, targetDir);
            Directory.CreateDirectory(dirPath);

            onProgress?.Invoke($"Checking Geyser compatibility for {serverType} {minecraftVersion}...");
            var geyserVersion = await _modrinth.GetLatestVersionAsync("geyser", minecraftVersion, loader);
            if (geyserVersion == null)
            {
                throw new InvalidOperationException(
                    $"Geyser does not currently support Minecraft {minecraftVersion} on {serverType}. " +
                    "Check https://modrinth.com/mod/geyser for supported versions.");
            }

            onProgress?.Invoke($"Downloading Geyser ({loader})...");
            string geyserPath = Path.Combine(dirPath, "Geyser.jar");
            await DownloadPrimaryFileAsync(geyserVersion, geyserPath, progress, cancellationToken);

            onProgress?.Invoke("Checking Floodgate compatibility...");
            var floodgateVersion = await _modrinth.GetLatestVersionAsync("floodgate", minecraftVersion, loader);
            if (floodgateVersion == null)
            {
                _logger.LogWarning("Floodgate not found for {ServerType} {McVersion}. Installing Geyser only.", serverType, minecraftVersion);
                onProgress?.Invoke($"Warning: Floodgate not available for {serverType} {minecraftVersion}. Geyser only.");
                onProgress?.Invoke("Warning: Floodgate not available — Geyser only.");
            }
            else
            {
                onProgress?.Invoke($"Downloading Floodgate ({loader})...");
                string floodgatePath = Path.Combine(dirPath, "Floodgate.jar");
                await DownloadPrimaryFileAsync(floodgateVersion, floodgatePath, progress, cancellationToken);
            }

            WriteConnectGuide(instancePath, targetDir);
            onProgress?.Invoke("Cross-play setup complete.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Geyser and Floodgate for {ServerType} {McVersion}.", serverType, minecraftVersion);
            throw new InvalidOperationException(
                $"Cross-play setup failed for {serverType} {minecraftVersion}. Try again later or install Geyser manually.", ex);
        }
    }

    private async Task DownloadPrimaryFileAsync(
        ModrinthVersion version,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files.FirstOrDefault();
        if (file == null)
        {
            throw new InvalidOperationException("Modrinth returned a version with no downloadable files.");
        }

        await _downloader.DownloadFileAsync(file.Url, destinationPath, null, progress, cancellationToken);
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

    private static string ResolveTargetDirectory(string loader)
    {
        return loader switch
        {
            "fabric" or "forge" or "neoforge" => "mods",
            _ => "plugins"
        };
    }

    private static bool IsAtLeastMinecraftVersion(string minecraftVersion, int major, int minor, int patch)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            return false;
        }

        string normalized = minecraftVersion.Split('-', 2)[0];
        string[] parts = normalized.Split('.');

        int vMajor = parts.Length > 0 && int.TryParse(parts[0], out int parsedMajor) ? parsedMajor : 0;
        int vMinor = parts.Length > 1 && int.TryParse(parts[1], out int parsedMinor) ? parsedMinor : 0;
        int vPatch = parts.Length > 2 && int.TryParse(parts[2], out int parsedPatch) ? parsedPatch : 0;

        if (vMajor != major)
        {
            return vMajor > major;
        }

        if (vMinor != minor)
        {
            return vMinor > minor;
        }

        return vPatch >= patch;
    }

    private void WriteConnectGuide(string instancePath, string targetDir)
    {
        try
        {
            string guidePath = Path.Combine(instancePath, "BEDROCK-CONNECT.txt");
            if (File.Exists(guidePath))
            {
                return;
            }

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
