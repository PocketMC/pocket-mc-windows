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

        return SelectVersionsAfterCurrent(versions, metadata.MinecraftVersion);
    }

    public async Task<IReadOnlyList<ModLoaderVersion>> GetAvailableLoaderVersionsAsync(
        InstanceMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        IServerSoftwareProvider provider = ResolveProvider(metadata.ServerType);
        List<ModLoaderVersion> builds = await provider.GetBuildsAsync(metadata.MinecraftVersion);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(metadata.LoaderVersion))
        {
            return builds.ToArray();
        }

        int currentIndex = builds.FindIndex(b => b.Version.Equals(metadata.LoaderVersion, StringComparison.OrdinalIgnoreCase));
        
        if (currentIndex > 0)
        {
            return builds.Take(currentIndex).ToArray();
        }
        else if (currentIndex == -1)
        {
            return builds.ToArray();
        }

        return Array.Empty<ModLoaderVersion>();
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

    private static IReadOnlyList<MinecraftVersion> SelectVersionsAfterCurrent(
        IReadOnlyList<MinecraftVersion> versions,
        string currentVersion)
    {
        var releaseVersions = versions
            .Where(version => !string.IsNullOrWhiteSpace(version.Id))
            .Where(version => version.Type.Equals("release", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(version.Type))
            .ToArray();

        if (JavaRuntimeResolver.TryParseVersion(currentVersion, out Version parsedCurrent))
        {
            return releaseVersions
                .Select(version => new
                {
                    Version = version,
                    Parsed = JavaRuntimeResolver.TryParseVersion(version.Id, out Version parsed)
                        ? parsed
                        : null
                })
                .Where(item => item.Parsed != null && item.Parsed > parsedCurrent)
                .OrderByDescending(item => item.Parsed)
                .Select(item => item.Version)
                .DistinctBy(version => version.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        int currentIndex = Array.FindIndex(releaseVersions, version =>
            version.Id.Equals(currentVersion, StringComparison.OrdinalIgnoreCase));
        if (currentIndex > 0)
        {
            return releaseVersions.Take(currentIndex).ToArray();
        }

        return Array.Empty<MinecraftVersion>();
    }
}
