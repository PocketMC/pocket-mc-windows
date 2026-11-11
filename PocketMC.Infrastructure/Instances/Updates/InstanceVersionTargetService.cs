using PocketMC.Application.Interfaces.Instances;
using PocketMC.Infrastructure.Java;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances.Updates;

public sealed class InstanceVersionTargetService
{
    private readonly IReadOnlyList<IServerSoftwareProvider> _providers;

    public InstanceVersionTargetService(IEnumerable<IServerSoftwareProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetAvailableTargetVersionsAsync(
        InstanceMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        IServerSoftwareProvider provider = ResolveProvider(metadata.ServerType);
        List<MinecraftVersion> versions = await provider.GetAvailableVersionsAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return versions
            .Where(version => !string.IsNullOrWhiteSpace(version.Id))
            .Where(version => version.Type.Equals("release", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(version.Type))
            .DistinctBy(version => version.Id, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(version => ParseSortableVersion(version.Id))
            .ThenByDescending(version => version.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Returns every loader build published for the given Minecraft version.</summary>
    public async Task<IReadOnlyList<ModLoaderVersion>> GetAvailableLoaderVersionsAsync(
        InstanceMetadata metadata,
        string? minecraftVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string targetVersion = string.IsNullOrWhiteSpace(minecraftVersion)
            ? metadata.MinecraftVersion
            : minecraftVersion;

        IServerSoftwareProvider provider = ResolveProvider(metadata.ServerType);
        List<ModLoaderVersion> builds = await provider.GetBuildsAsync(targetVersion);
        cancellationToken.ThrowIfCancellationRequested();

        return builds
            .Where(build => !string.IsNullOrWhiteSpace(build.Version))
            .DistinctBy(build => build.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Version ParseSortableVersion(string versionId)
    {
        return JavaRuntimeResolver.TryParseVersion(versionId, out Version parsed) ? parsed : new Version(0, 0);
    }

    private IServerSoftwareProvider ResolveProvider(string serverType)
    {
        string normalizedServerType = serverType ?? "Vanilla";

        if (normalizedServerType.StartsWith("Paper", StringComparison.OrdinalIgnoreCase) ||
            normalizedServerType.StartsWith("Spigot", StringComparison.OrdinalIgnoreCase))
        {
            return FindProvider("Paper");
        }

        if (normalizedServerType.StartsWith("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return FindProvider("Fabric");
        }

        if (normalizedServerType.StartsWith("Forge", StringComparison.OrdinalIgnoreCase))
        {
            return FindProvider("Forge");
        }

        if (normalizedServerType.StartsWith("NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            return FindProvider("NeoForge");
        }

        if (normalizedServerType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            return FindProvider("Bedrock");
        }

        if (normalizedServerType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
        {
            return FindProvider("Pocketmine");
        }

        return FindProvider("Vanilla");
    }

    private IServerSoftwareProvider FindProvider(string displayNamePrefix)
    {
        IServerSoftwareProvider? provider = _providers.FirstOrDefault(provider =>
            provider.DisplayName.StartsWith(displayNamePrefix, StringComparison.OrdinalIgnoreCase));

        return provider ?? throw new InvalidOperationException($"No {displayNamePrefix} version provider is registered.");
    }
}
