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
    /// All downloads go through Modrinth, respecting server type and MC version.
    ///
    /// Deliberately does NOT pre-write config.yml — Geyser auto-generates a correct
    /// one on first run. A hand-crafted config risks schema mismatches with the
    /// installed Geyser build and will break plugin startup.
    ///
    /// Connection info after first server run:
    ///   - Bedrock clients connect on the SAME IP as Java, port 19132 (UDP)
    ///   - Config lives in: plugins/Geyser-Spigot/config.yml (or mods/ equivalent)
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
            // --- Defensive: block known-unsupported combinations ---
            if (serverType.StartsWith("Vanilla", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Geyser requires Paper, Fabric, or Forge. Vanilla is not supported.");

            if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) ||
                serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bedrock and PocketMine servers do not need Geyser.");

            // --- Resolve Modrinth loader string ---
            string loader = ResolveLoader(serverType, minecraftVersion);
            string targetDir = loader is "fabric" or "forge" or "neoforge" ? "mods" : "plugins";
            string dirPath = Path.Combine(instancePath, targetDir);
            Directory.CreateDirectory(dirPath);

            _logger.LogInformation(
                "Provisioning Geyser+Floodgate via Modrinth: server={ServerType}, mc={McVer}, loader={Loader}, dir={Dir}",
                serverType, minecraftVersion, loader, targetDir);

            // --- Geyser ---
            ReportStatus(progress, $"Checking Geyser compatibility for {serverType} {minecraftVersion}...");
            var geyserVersion = await _modrinth.GetLatestVersionAsync("geyser", minecraftVersion, loader);

            if (geyserVersion == null)
            {
                throw new InvalidOperationException(
                    $"Geyser does not currently support Minecraft {minecraftVersion} on {serverType}. " +
                    $"Check https://modrinth.com/mod/geyser for supported versions.");
            }

            var geyserFile = geyserVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? geyserVersion.Files.FirstOrDefault();
            if (geyserFile == null)
                throw new InvalidOperationException("Modrinth returned a Geyser version with no downloadable files.");

            string geyserPath = Path.Combine(dirPath, "Geyser.jar");
            ReportStatus(progress, $"Downloading Geyser ({loader})...");
            _logger.LogInformation("Downloading Geyser from {Url}", geyserFile.Url);
            await _downloader.DownloadFileAsync(geyserFile.Url, geyserPath, null, progress, cancellationToken);

            // --- Floodgate (optional — Geyser can run without it) ---
            ReportStatus(progress, "Checking Floodgate compatibility...");
            var floodgateVersion = await _modrinth.GetLatestVersionAsync("floodgate", minecraftVersion, loader);

            if (floodgateVersion == null)
            {
                _logger.LogWarning(
                    "Floodgate not found for {ServerType} {McVersion} (loader={Loader}). Installing Geyser only.",
                    serverType, minecraftVersion, loader);
                ReportStatus(progress, $"Warning: Floodgate not available for {serverType} {minecraftVersion}. Geyser only.");
            }
            else
            {
                var floodgateFile = floodgateVersion.Files.FirstOrDefault(f => f.IsPrimary) ?? floodgateVersion.Files.FirstOrDefault();
                if (floodgateFile == null)
                {
                    _logger.LogWarning("Modrinth returned a Floodgate version with no files. Skipping Floodgate.");
                    ReportStatus(progress, "Warning: Floodgate version has no downloadable files. Geyser only.");
                }
                else
                {
                    string floodgatePath = Path.Combine(dirPath, "Floodgate.jar");
                    ReportStatus(progress, $"Downloading Floodgate ({loader})...");
                    _logger.LogInformation("Downloading Floodgate from {Url}", floodgateFile.Url);
                    await _downloader.DownloadFileAsync(floodgateFile.Url, floodgatePath, null, progress, cancellationToken);
                }
            }

            // Write a README so users know how to connect Bedrock clients
            WriteConnectGuide(instancePath, targetDir);

            ReportStatus(progress, "Cross-play setup complete.");
            _logger.LogInformation("Geyser provisioning complete for {ServerType} {McVersion}.", serverType, minecraftVersion);
        }
        catch (InvalidOperationException)
        {
            // Compatibility failures — surface cleanly to caller
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
                $"Cross-play setup failed: {ex.Message}. " +
                $"Your server was created but Geyser/Floodgate could not be installed.", ex);
        }
    }

    /// <summary>
    /// Maps server type + MC version to the Modrinth loader string.
    /// </summary>
    private static string ResolveLoader(string serverType, string minecraftVersion)
    {
        return serverType.ToLowerInvariant() switch
        {
            "paper" or "spigot" => "spigot",
            "fabric"            => "fabric",
            "forge"             => IsNewerOrEqual(minecraftVersion, "1.20.5") ? "neoforge" : "forge",
            _                   => "spigot" // safe default
        };
    }

    /// <summary>
    /// Compares two Minecraft version strings (e.g. "1.20.5" >= "1.20.5").
    /// Returns true if <paramref name="version"/> >= <paramref name="threshold"/>.
    /// </summary>
    private static bool IsNewerOrEqual(string version, string threshold)
    {
        try
        {
            var vParts = version.Split('.').Select(int.Parse).ToArray();
            var tParts = threshold.Split('.').Select(int.Parse).ToArray();

            int len = Math.Max(vParts.Length, tParts.Length);
            for (int i = 0; i < len; i++)
            {
                int v = i < vParts.Length ? vParts[i] : 0;
                int t = i < tParts.Length ? tParts[i] : 0;
                if (v > t) return true;
                if (v < t) return false;
            }

            return true; // equal
        }
        catch
        {
            // If version string is unparseable (snapshot, etc.), default to false
            return false;
        }
    }

    /// <summary>
    /// Reports a status string to the UI via the download progress reporter.
    /// Uses a zero-byte progress report so the UI can display the message.
    /// </summary>
    private static void ReportStatus(IProgress<DownloadProgress>? progress, string _)
    {
        // The DownloadProgress struct doesn't carry a text field, so we just
        // reset the progress bar to indeterminate (0/0) to signal a status change.
        // The calling code in NewInstancePage sets TxtProgress.Text before calling us.
        progress?.Report(new DownloadProgress { BytesRead = 0, TotalBytes = 0 });
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
