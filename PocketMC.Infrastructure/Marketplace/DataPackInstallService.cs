using PocketMC.Application.Services.Instances;
using PocketMC.Domain.Models;
using PocketMC.Domain.Security;
using PocketMC.Domain.Storage;

namespace PocketMC.Infrastructure.Marketplace;

/// <summary>Installs and updates Java data packs from Modrinth/CurseForge, tracking provenance in the add-on manifest.</summary>
public sealed class DataPackInstallService
{
    private readonly IReadOnlyList<IAddonProvider> _providers;
    private readonly MarketplaceFileInstaller _installer;
    private readonly AddonManifestService _manifest;

    public DataPackInstallService(IEnumerable<IAddonProvider> providers, MarketplaceFileInstaller installer, AddonManifestService manifest)
    {
        _providers = providers.ToList();
        _installer = installer;
        _manifest = manifest;
    }

    private IAddonProvider? Provider(string name) =>
        _providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Installs the latest compatible version and returns the installed file name.</summary>
    public async Task<string> InstallAsync(string serverDir, string worldDir, string providerName, string projectId,
        string mcVersion, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var provider = Provider(providerName) ?? throw new InvalidOperationException($"Unknown provider '{providerName}'.");
        var version = await provider.GetLatestVersionAsync(projectId, Normalize(mcVersion), Loaders(providerName))
            ?? throw new InvalidOperationException("No compatible data pack version was found.");
        return await InstallVersionAsync(serverDir, worldDir, providerName, version, mcVersion, replacing: null, progress, ct);
    }

    /// <summary>Returns the newer version when the tracked pack is out of date, otherwise null.</summary>
    public async Task<MarketplaceVersion?> CheckUpdateAsync(string serverDir, string fileName, string mcVersion)
    {
        var entry = (await _manifest.LoadManifestAsync(serverDir)).Entries
            .FirstOrDefault(e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        var provider = entry == null ? null : Provider(entry.Provider);
        if (entry == null || provider == null) return null;

        var latest = await provider.GetLatestVersionAsync(entry.ProjectId, Normalize(mcVersion), Loaders(entry.Provider));
        return latest != null && latest.Id != entry.VersionId ? latest : null;
    }

    /// <summary>"Provider:ProjectId" keys of tracked packs whose file is still in the world.</summary>
    public HashSet<string> InstalledKeys(string serverDir, string worldDir)
    {
        var files = DataPackLibrary.List(worldDir).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _manifest.LoadManifest(serverDir).Entries
            .Where(e => files.Contains(e.FileName))
            .Select(e => $"{e.Provider}:{e.ProjectId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Deletes a data pack and its manifest entry.</summary>
    public async Task RemoveAsync(string serverDir, string worldDir, string fileName)
    {
        DataPackLibrary.Remove(worldDir, fileName);
        await _manifest.UnregisterByFileNameAsync(serverDir, fileName);
    }

    /// <summary>Installs <paramref name="latest"/> over the tracked pack, keeping its enabled/disabled state.</summary>
    public async Task<string> UpdateAsync(string serverDir, string worldDir, string oldFileName, MarketplaceVersion latest,
        string mcVersion, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var entry = (await _manifest.LoadManifestAsync(serverDir)).Entries
            .FirstOrDefault(e => e.FileName.Equals(oldFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This data pack was not installed from a marketplace.");
        bool wasEnabled = DataPackLibrary.List(worldDir).FirstOrDefault(p => p.Name == oldFileName)?.IsEnabled ?? true;

        string newName = await InstallVersionAsync(serverDir, worldDir, entry.Provider, latest, mcVersion, replacing: oldFileName, progress, ct);
        if (!newName.Equals(oldFileName, StringComparison.OrdinalIgnoreCase))
            DataPackLibrary.Remove(worldDir, oldFileName);
        if (!wasEnabled) DataPackLibrary.SetEnabled(worldDir, newName, false);
        return newName;
    }

    private async Task<string> InstallVersionAsync(string serverDir, string worldDir, string providerName,
        MarketplaceVersion version, string mcVersion, string? replacing, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        string name = MarketplaceFileNameSanitizer.RequireSafeFileName(version.FileName);
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Data packs must be .zip files.");
        // Only the pack being updated may be replaced in place; anything else with the same name is a different pack.
        if (!name.Equals(replacing, StringComparison.OrdinalIgnoreCase) && DataPackLibrary.List(worldDir).Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A data pack named '{name}' already exists in this world.");
        string dest = PathSafety.ValidateContainedPath(Path.Combine(worldDir, DataPackLibrary.ActiveFolder), name)
            ?? throw new InvalidOperationException("Unsafe data pack file name.");

        await _installer.InstallAsync(version.DownloadUrl, dest, version.Hash, version.HashType, progress, ct);
        await _manifest.RegisterInstallAsync(serverDir, providerName, version.ProjectId, version.Id, name,
            version.ProjectTitle, version.IconUrl, null, fileHash: version.Hash, fileHashType: version.HashType,
            minecraftVersion: Normalize(mcVersion), downloadUrl: version.DownloadUrl);
        return name;
    }

    // Modrinth tags real data packs with the "datapack" loader; CurseForge already scopes by class, so no loader there.
    private static IReadOnlyList<string> Loaders(string provider) =>
        provider.Equals("Modrinth", StringComparison.OrdinalIgnoreCase) ? new[] { "datapack" } : Array.Empty<string>();

    private static string Normalize(string mc) => mc == "*" ? "" : mc;
}
