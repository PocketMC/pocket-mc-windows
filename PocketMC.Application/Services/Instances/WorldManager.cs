using PocketMC.Domain.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Security;

using PocketMC.Domain.Storage;

namespace PocketMC.Application.Services.Instances;

/// <summary>
/// Handles intelligent world archive extraction for both Java and Bedrock servers.
///
/// Supported input formats:
/// <list type="bullet">
///   <item><c>.zip</c> — standard world archive (Java or Bedrock)</item>
///   <item><c>.mcworld</c> — Bedrock world export (renamed ZIP)</item>
/// </list>
///
/// Detection strategy:
/// <list type="bullet">
///   <item>Recursively hunts for <c>level.dat</c> to find the true world root,
///         regardless of how the archive was packaged.</item>
/// </list>
/// </summary>
public class WorldManager
{
    private readonly ILogger<WorldManager> _logger;

    public WorldManager(ILogger<WorldManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts a world archive (.zip or .mcworld) to the target path,
    /// intelligently finding the real world root.
    /// </summary>
    /// <param name="archivePath">
    /// Path to the world archive. Accepts <c>.zip</c> and <c>.mcworld</c> files
    /// (both are ZIP-format internally).
    /// </param>
    /// <param name="targetWorldPath">
    /// Absolute path where the world should be installed.
    /// Callers are responsible for resolving the correct engine-specific path
    /// (e.g. <c>serverDir/world</c> for Java, <c>serverDir/worlds/LevelName</c> for Bedrock).
    /// Use <see cref="WorldPathResolver"/> to compute this.
    /// </param>
    /// <param name="onProgress">Optional progress callback.</param>
    public async Task ImportWorldZipAsync(string archivePath, string targetWorldPath, Action<string>? onProgress = null)
    {
        ValidateArchiveExtension(archivePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "PocketMC", $"Extraction-{Guid.NewGuid()}");

        try
        {
            onProgress?.Invoke("Extracting world archive...");
            await SafeZipExtractor.ExtractAsync(archivePath, tempDir);

            onProgress?.Invoke("Scanning for level.dat...");
            string? worldRoot = await Task.Run(() => FindWorldRoot(tempDir));

            if (worldRoot == null)
            {
                throw new InvalidOperationException(
                    "Could not find level.dat in the archive. This doesn't appear to be a valid Minecraft world.");
            }

            _logger.LogInformation(
                "World root detected at '{WorldRoot}' inside archive. Target: '{Target}'.",
                Path.GetRelativePath(tempDir, worldRoot), targetWorldPath);

            // Ensure the parent directory exists (critical for Bedrock: worlds/ may not exist yet)
            string? parentDir = Path.GetDirectoryName(targetWorldPath);
            if (parentDir != null)
            {
                Directory.CreateDirectory(parentDir);
            }

            // Clean existing world directory
            if (Directory.Exists(targetWorldPath))
            {
                onProgress?.Invoke("Removing existing world...");
                await FileUtils.CleanDirectoryAsync(targetWorldPath);
            }

            // Copy the true world root to the target
            onProgress?.Invoke("Installing world...");
            await FileUtils.CopyDirectoryAsync(worldRoot, targetWorldPath);

            onProgress?.Invoke("World imported successfully!");
        }
        finally
        {
            // Always clean up temp directory
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up temporary world extraction directory {TempDir}.", tempDir);
                }
            }
        }
    }

    /// <summary>
    /// Validates the archive file extension. Accepts .zip and .mcworld.
    /// </summary>
    private static void ValidateArchiveExtension(string archivePath)
    {
        string ext = Path.GetExtension(archivePath);
        if (!ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".mcworld", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Unsupported archive format '{ext}'. Expected .zip or .mcworld.");
        }
    }

    /// <summary>
    /// Recursively searches for level.dat and returns its parent directory (the true world root).
    /// </summary>
    private string? FindWorldRoot(string searchDir)
    {
        // Check current directory
        if (File.Exists(Path.Combine(searchDir, "level.dat")))
            return searchDir;

        // Search subdirectories
        foreach (var subDir in Directory.GetDirectories(searchDir))
        {
            var result = FindWorldRoot(subDir);
            if (result != null) return result;
        }

        return null;
    }
}

