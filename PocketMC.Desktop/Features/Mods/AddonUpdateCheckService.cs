using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Mods;

public sealed class AddonUpdateCheckService
{
    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Modrinth",
        "CurseForge"
    };

    private readonly AddonManifestService _manifestService;
    private readonly AddonUpdateService _updateService;
    private readonly ModrinthService? _modrinth;
    private readonly CurseForgeService? _curseForge;
    private readonly ILogger<AddonUpdateCheckService> _logger;

    public AddonUpdateCheckService(
        AddonManifestService manifestService,
        AddonUpdateService updateService,
        ILogger<AddonUpdateCheckService>? logger = null,
        ModrinthService? modrinth = null,
        CurseForgeService? curseForge = null)
    {
        _manifestService = manifestService;
        _updateService = updateService;
        _modrinth = modrinth;
        _curseForge = curseForge;
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
            entry = await DiscoverAddonByHashAsync(instanceRoot, item, metadata).ConfigureAwait(false);
            if (entry == null)
            {
                return new AddonUpdateCheckResultModel
                {
                    Status = AddonUpdateStatus.UnknownSource,
                    Message = "This add-on was not installed from a known marketplace."
                };
            }
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

    private async Task<AddonManifestEntry?> DiscoverAddonByHashAsync(
        string instanceRoot,
        AddonInventoryItem item,
        InstanceMetadata metadata)
    {
        if (!File.Exists(item.FullPath))
        {
            return null;
        }

        try
        {
            string sha1 = await CalculateSha1Async(item.FullPath).ConfigureAwait(false);
            long murmur = ComputeMurmur2Hash(item.FullPath);

            _logger.LogInformation("Attempting hash lookup for {FileName} (SHA-1: {Sha1}, Murmur2: {Murmur})", item.FileName, sha1, murmur);

            string loader = ResolveLoader(metadata.Compatibility, item);
            var loaderCandidates = string.IsNullOrEmpty(loader) ? Array.Empty<string>() : new[] { loader };

            // 1. Try Modrinth via SHA-1
            if (_modrinth != null)
            {
                try
                {
                    var modrinthVersion = await _modrinth.GetVersionByHashAsync(sha1, "sha1", loaderCandidates).ConfigureAwait(false);
                    if (modrinthVersion != null)
                    {
                        _logger.LogInformation("Found addon {FileName} on Modrinth. ProjectId: {ProjectId}, VersionId: {VersionId}", item.FileName, modrinthVersion.ProjectId, modrinthVersion.Id);
                        
                        await _manifestService.RegisterInstallAsync(
                            instanceRoot,
                            "Modrinth",
                            modrinthVersion.ProjectId,
                            modrinthVersion.Id,
                            item.FileName,
                            modrinthVersion.ProjectTitle,
                            modrinthVersion.IconUrl,
                            modrinthVersion.Name,
                            modrinthVersion.ClientSide,
                            modrinthVersion.ServerSide,
                            sha1,
                            "sha1",
                            metadata.MinecraftVersion,
                            loader,
                            modrinthVersion.DownloadUrl
                        ).ConfigureAwait(false);

                        var manifest = await _manifestService.LoadManifestAsync(instanceRoot).ConfigureAwait(false);
                        return FindManifestEntry(manifest, item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Modrinth hash lookup failed for {FileName}", item.FileName);
                }
            }

            // 2. Try CurseForge via Murmur2
            if (_curseForge != null)
            {
                try
                {
                    var cfVersion = await _curseForge.GetVersionByFingerprintAsync(murmur).ConfigureAwait(false);
                    if (cfVersion != null)
                    {
                        _logger.LogInformation("Found addon {FileName} on CurseForge. ProjectId: {ProjectId}, VersionId: {VersionId}", item.FileName, cfVersion.ProjectId, cfVersion.Id);

                        await _manifestService.RegisterInstallAsync(
                            instanceRoot,
                            "CurseForge",
                            cfVersion.ProjectId,
                            cfVersion.Id,
                            item.FileName,
                            cfVersion.ProjectTitle,
                            cfVersion.IconUrl,
                            cfVersion.Name,
                            cfVersion.ClientSide,
                            cfVersion.ServerSide,
                            cfVersion.Hash,
                            cfVersion.HashType,
                            metadata.MinecraftVersion,
                            loader,
                            cfVersion.DownloadUrl
                        ).ConfigureAwait(false);

                        var manifest = await _manifestService.LoadManifestAsync(instanceRoot).ConfigureAwait(false);
                        return FindManifestEntry(manifest, item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CurseForge fingerprint lookup failed for {FileName}", item.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while performing hash-based discovery for {FileName}", item.FileName);
        }

        return null;
    }

    private static async Task<string> CalculateSha1Async(string filePath)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        byte[] hashBytes = await sha1.ComputeHashAsync(stream).ConfigureAwait(false);
        var sb = new System.Text.StringBuilder();
        foreach (byte b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static long ComputeMurmur2Hash(string filePath)
    {
        const uint m = 0x5bd1e995;
        const int r = 24;

        // Pass 1: Count non-whitespace bytes
        int size = 0;
        byte[] buffer = new byte[8192];
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192))
        {
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    if (b != 9 && b != 10 && b != 13 && b != 32)
                    {
                        size++;
                    }
                }
            }
        }

        uint h = 1 ^ (uint)size;

        // Pass 2: Compute hash
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192))
        {
            int bytesRead;
            int index = 0;
            byte[] chunk = new byte[4];

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    if (b != 9 && b != 10 && b != 13 && b != 32)
                    {
                        chunk[index++] = b;
                        if (index == 4)
                        {
                            uint k = (uint)(chunk[0] | (chunk[1] << 8) | (chunk[2] << 16) | (chunk[3] << 24));
                            k *= m;
                            k ^= k >> r;
                            k *= m;

                            h *= m;
                            h ^= k;
                            index = 0;
                        }
                    }
                }
            }

            // Remaining bytes
            switch (index)
            {
                case 3:
                    h ^= (uint)chunk[2] << 16;
                    goto case 2;
                case 2:
                    h ^= (uint)chunk[1] << 8;
                    goto case 1;
                case 1:
                    h ^= chunk[0];
                    h *= m;
                    break;
            }
        }

        h ^= h >> 13;
        h *= m;
        h ^= h >> 15;

        return (long)h;
    }
}

