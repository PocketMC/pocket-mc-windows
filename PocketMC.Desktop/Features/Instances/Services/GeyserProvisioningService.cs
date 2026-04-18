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
        CancellationToken cancellationToken = default,
        Action<string>? onProgress = null)
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

            string loader = GetLoaderForServer(serverType, minecraftVersion);
            string targetDir = GetTargetDirectory(loader);
            string dirPath = Path.Combine(instancePath, targetDir);
            Directory.CreateDirectory(dirPath);

            onProgress?.Invoke($"Checking Geyser compatibility for {serverType} {minecraftVersion}...");
            _logger.LogInformation("Checking Geyser compatibility for {ServerType} {MinecraftVersion} using loader {Loader}.", serverType, minecraftVersion, loader);

            var geyserVersion = await _modrinth.GetLatestVersionAsync("geyser", minecraftVersion, loader);
            if (geyserVersion == null)
            {
                throw new InvalidOperationException(
                    $"Geyser does not currently support Minecraft {minecraftVersion} on {serverType}. " +
                    "Check https://modrinth.com/mod/geyser for supported versions.");
            }

            var geyserFile = geyserVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? geyserVersion.Files.FirstOrDefault();
            if (geyserFile == null)
            {
                throw new InvalidOperationException("Modrinth returned a version with no downloadable files.");
            }

            onProgress?.Invoke($"Downloading Geyser ({loader})...");
            await _downloader.DownloadFileAsync(
                geyserFile.Url,
                Path.Combine(dirPath, "Geyser.jar"),
                null,
                progress,
                cancellationToken);

            onProgress?.Invoke("Checking Floodgate compatibility...");
            var floodgateVersion = await _modrinth.GetLatestVersionAsync("floodgate", minecraftVersion, loader);

            if (floodgateVersion == null)
            {
                _logger.LogWarning("Floodgate not found for {ServerType} {McVersion}. Installing Geyser only.", serverType, minecraftVersion);
                onProgress?.Invoke("Warning: Floodgate not available — Geyser only.");
                onProgress?.Invoke("Cross-play setup complete.");
                return;
            }

            var floodgateFile = floodgateVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? floodgateVersion.Files.FirstOrDefault();
            if (floodgateFile == null)
            {
                throw new InvalidOperationException("Modrinth returned a version with no downloadable files.");
            }

            onProgress?.Invoke($"Downloading Floodgate ({loader})...");
            await _downloader.DownloadFileAsync(
                floodgateFile.Url,
                Path.Combine(dirPath, "Floodgate.jar"),
                null,
                progress,
                cancellationToken);

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
                $"Cross-play setup could not be completed for {serverType} {minecraftVersion}. Try again later.",
                ex);
        }
    }

    private static string GetTargetDirectory(string loader)
    {
        return loader switch
        {
            "fabric" or "forge" or "neoforge" => "mods",
            _ => "plugins"
        };
    }

    private static string GetLoaderForServer(string serverType, string minecraftVersion)
    {
        if (string.Equals(serverType, "Paper", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(serverType, "Spigot", StringComparison.OrdinalIgnoreCase))
        {
            return "spigot";
        }

        if (string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return "fabric";
        }

        if (string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase))
        {
            return IsMinecraftVersionAtLeast(minecraftVersion, 1, 20, 5) ? "neoforge" : "forge";
        }

        return "spigot";
    }

    private static bool IsMinecraftVersionAtLeast(string version, int major, int minor, int patch)
    {
        var segments = version.Split('.', '-', StringSplitOptions.RemoveEmptyEntries)
            .Take(3)
            .Select(ParseLeadingInt)
            .ToArray();

        int actualMajor = segments.Length > 0 ? segments[0] : 0;
        int actualMinor = segments.Length > 1 ? segments[1] : 0;
        int actualPatch = segments.Length > 2 ? segments[2] : 0;

        if (actualMajor != major)
        {
            return actualMajor > major;
        }

        if (actualMinor != minor)
        {
            return actualMinor > minor;
        }

        return actualPatch >= patch;
    }

    private static int ParseLeadingInt(string value)
    {
        var digits = value.TakeWhile(char.IsDigit).ToArray();
        return digits.Length == 0 ? 0 : int.Parse(new string(digits));
    }
}
