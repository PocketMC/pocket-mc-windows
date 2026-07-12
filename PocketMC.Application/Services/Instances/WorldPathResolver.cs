using System;
using System.IO;
using System.Linq;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Instances;

/// <summary>
/// Centralised world-directory resolution for all engine families.
///
/// Java engines  → <c>&lt;serverDir&gt;/world/</c>
/// BDS (Bedrock) → <c>&lt;serverDir&gt;/worlds/&lt;level-name&gt;/</c>  (default: "Bedrock level")
/// PocketMine    → <c>&lt;serverDir&gt;/worlds/</c>
///
/// This matches the conventions already used by <see cref="BackupService"/>
/// and <see cref="Mods.BedrockAddonInstaller"/>.
/// </summary>
public static class WorldPathResolver
{
    private const string DefaultBedrockLevelName = "Bedrock level";

    /// <summary>
    /// Resolves the absolute world directory path for the given server instance.
    /// </summary>
    /// <param name="serverDir">Root directory of the server instance.</param>
    /// <param name="metadata">Instance metadata (used for <see cref="InstanceMetadata.Compatibility"/>).</param>
    /// <param name="configService">Config service to read <c>level-name</c> from server.properties (may be null for Java).</param>
    public static string Resolve(
        string serverDir,
        InstanceMetadata metadata,
        ServerConfigurationService? configService = null)
    {
        return metadata.Compatibility.Family switch
        {
            EngineFamily.Bedrock => ResolveBedrockWorldPath(serverDir, configService),
            EngineFamily.Pocketmine => Path.Combine(serverDir, "worlds"),
            _ => Path.Combine(serverDir, "world")
        };
    }

    /// <summary>
    /// Returns the relative world folder name (for display / logging).
    /// </summary>
    public static string GetRelativeFolderName(
        InstanceMetadata metadata,
        ServerConfigurationService? configService,
        string serverDir)
    {
        return metadata.Compatibility.Family switch
        {
            EngineFamily.Bedrock => Path.Combine("worlds", GetBedrockLevelName(serverDir, configService)),
            EngineFamily.Pocketmine => "worlds",
            _ => "world"
        };
    }

    // ── Bedrock helpers ───────────────────────────────────────────────────

    private static string ResolveBedrockWorldPath(string serverDir, ServerConfigurationService? configService)
    {
        string levelName = GetBedrockLevelName(serverDir, configService);
        string preferred = Path.Combine(serverDir, "worlds", levelName);

        // If the preferred directory exists, use it.
        if (Directory.Exists(preferred))
            return preferred;

        // Fall back to the first existing world directory under worlds/.
        string worldsParent = Path.Combine(serverDir, "worlds");
        if (Directory.Exists(worldsParent))
        {
            string? first = Directory.GetDirectories(worldsParent).FirstOrDefault();
            if (first != null)
                return first;
        }

        // Nothing exists yet — return the preferred path (caller decides whether to create).
        return preferred;
    }

    private static string GetBedrockLevelName(string serverDir, ServerConfigurationService? configService)
    {
        if (configService != null &&
            configService.TryGetProperty(serverDir, "level-name", out var levelName) &&
            !string.IsNullOrWhiteSpace(levelName))
        {
            return levelName.Trim();
        }

        return DefaultBedrockLevelName;
    }
}
