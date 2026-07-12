using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Application.Services.Instances;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Mods;

/// <summary>
/// Engine-agnostic contract for addon (plugin/mod/pack) management.
/// Implementations are expected to be engine-specific:
/// <list type="bullet">
///   <item><see cref="PocketMC.Desktop.Features.Mods.JavaAddonManager"/> — JAR-based plugins/mods for Paper, Fabric, Forge.</item>
///   <item><see cref="PocketMC.Desktop.Features.Mods.PocketmineAddonManager"/> — <c>.phar</c> plugins.</item>
///   <item><see cref="PocketMC.Desktop.Features.Mods.BedrockAddonInstaller"/> — <c>.mcpack</c>/<c>.mcaddon</c> installed to behavior/resource packs.</item>
/// </list>
/// </summary>
public interface IAddonManager
{
    /// <summary>Engine identifier string this manager handles (e.g. "Bedrock", "Pocketmine", "Paper").</summary>
    string EngineKey { get; }

    /// <summary>
    /// Returns the list of currently installed addons in the given server directory.
    /// </summary>
    IReadOnlyList<AddonInfo> GetInstalledAddons(string serverDir);

    /// <summary>
    /// Installs a single addon from a local file path.  Engine-specific logic
    /// (e.g. extracting manifests, updating world pack JSON) is handled internally.
    /// </summary>
    Task InstallAsync(string sourceFilePath, string serverDir, CancellationToken ct = default);

    /// <summary>
    /// Removes an installed addon by its file path or identifier.
    /// </summary>
    Task UninstallAsync(string addonPathOrId, string serverDir, CancellationToken ct = default);
}

/// <summary>
/// Lightweight representation of a discovered addon entry.
/// </summary>
public sealed class AddonInfo
{
    /// <summary>Display name derived from the manifest or filename.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Absolute path on disk.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>File size in kilobytes.</summary>
    public double SizeKb { get; init; }

    /// <summary>Last modified timestamp.</summary>
    public System.DateTime LastModified { get; init; }

    /// <summary>Engine-specific sub-type (e.g. "behavior", "resource", "plugin").</summary>
    public string AddonType { get; init; } = string.Empty;
}
