using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Mods;

public sealed class AddonUpdateCheckService
{
    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Modrinth",
        "CurseForge",
        "Poggit"
    };

    private readonly AddonManifestService _manifestService;
    private readonly AddonUpdateService _updateService;
    private readonly ILogger<AddonUpdateCheckService> _logger;

    public AddonUpdateCheckService(
        AddonManifestService manifestService,
        AddonUpdateService updateService,
        ILogger<AddonUpdateCheckService>? logger = null)
    {
        _manifestService = manifestService;
        _updateService = updateService;
        _logger = logger ?? NullLogger<AddonUpdateCheckService>.Instance;
    }

    public async Task<AddonUpdateCheckResultModel> CheckAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        AddonInventoryItem item,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddonManifest manifest = await _manifestService.LoadManifestAsync(instanceRoot);
        AddonManifestEntry? entry = FindManifestEntry(manifest, item);
        if (entry == null || IsUnknownSource(entry.Provider))
        {
            return new AddonUpdateCheckResultModel
            {
                Status = AddonUpdateStatus.UnknownSource,
                Message = "This add-on was not installed from a known marketplace."
            };
        }

        if (!SupportedProviders.Contains(entry.Provider))
        {
            return new AddonUpdateCheckResultModel
            {
                Status = AddonUpdateStatus.UnsupportedProvider,
                Message = $"Update checks are not supported for provider '{entry.Provider}'."
            };
        }

        if (!IsKindCompatible(metadata.Compatibility, item.Kind))
        {
            return new AddonUpdateCheckResultModel
            {
                Status = AddonUpdateStatus.PossiblyIncompatible,
                Message = "This add-on type does not match the current server engine."
            };
        }

        string loader = ResolveLoader(metadata.Compatibility, item);
        if (loader.Length == 0)
        {
            return new AddonUpdateCheckResultModel
            {
                Status = AddonUpdateStatus.PossiblyIncompatible,
                Message = "This add-on loader does not match the current server engine."
            };
        }

        try
        {
            AddonUpdateCheckResult providerResult = await _updateService.CheckForUpdateFromEntryAsync(
                entry,
                metadata.MinecraftVersion,
                loader,
                metadata.Compatibility);
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(providerResult.Error))
            {
                return new AddonUpdateCheckResultModel
                {
                    Status = AddonUpdateStatus.ProviderError,
                    Message = providerResult.Error
                };
            }

            AddonUpdateInfo info = new()
            {
                LatestVersionId = providerResult.LatestVersionId,
                LatestVersionName = providerResult.LatestVersionName,
                LatestFileName = providerResult.LatestFileName,
                LatestDownloadUrl = providerResult.LatestDownloadUrl,
                ProjectTitle = providerResult.ProjectTitle,
                Hash = providerResult.Hash,
                HashType = providerResult.HashType,
                ReleaseType = providerResult.ReleaseType,
                Warnings = providerResult.Warnings.ToArray()
            };

            return new AddonUpdateCheckResultModel
            {
                Status = providerResult.IsUpdateAvailable ? AddonUpdateStatus.UpdateAvailable : AddonUpdateStatus.UpToDate,
                Message = providerResult.IsUpdateAvailable
                    ? $"Update available: {providerResult.LatestVersionName ?? providerResult.LatestVersionId ?? "new version"}"
                    : "Up to date",
                UpdateInfo = info
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Passive update check failed for {RelativePath}.", item.RelativePath);
            return new AddonUpdateCheckResultModel
            {
                Status = AddonUpdateStatus.ProviderError,
                Message = "The provider could not be reached for this update check."
            };
        }
    }

    private static AddonManifestEntry? FindManifestEntry(AddonManifest manifest, AddonInventoryItem item)
    {
        return manifest.Entries.FirstOrDefault(entry =>
            entry.FileName.Equals(item.FileName, StringComparison.OrdinalIgnoreCase) ||
            entry.FileName.Equals(Path.GetFileName(item.RelativePath), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnknownSource(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider) ||
               provider.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKindCompatible(EngineCompatibility compatibility, AddonKind kind)
    {
        return kind switch
        {
            AddonKind.Plugin => compatibility.SupportsPlugins,
            AddonKind.Mod => compatibility.SupportsMods,
            _ => false
        };
    }

    private static string ResolveLoader(EngineCompatibility compatibility, AddonInventoryItem item)
    {
        if (item.Kind == AddonKind.Plugin)
        {
            return compatibility.LoaderName;
        }

        string loader = item.LoaderType.ToLowerInvariant();
        if (loader is "fabric" or "quilt" or "forge" or "neoforge")
        {
            return compatibility.CompatibleLoaderNames.Contains(loader, StringComparer.OrdinalIgnoreCase)
                ? loader
                : "";
        }

        return compatibility.LoaderName;
    }
}
