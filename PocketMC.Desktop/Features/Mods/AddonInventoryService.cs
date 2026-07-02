using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Mods;

public sealed class AddonInventoryService
{
    private readonly AddonManifestService _manifestService;
    private readonly AddonStateStore _stateStore;
    private readonly IServerLifecycleService? _lifecycleService;
    private readonly ILogger<AddonInventoryService> _logger;
    private readonly InstanceRegistry? _registry;

    public AddonInventoryService(
        AddonManifestService manifestService,
        AddonStateStore stateStore,
        IServerLifecycleService? lifecycleService = null,
        ILogger<AddonInventoryService>? logger = null,
        InstanceRegistry? registry = null)
    {
        _manifestService = manifestService;
        _stateStore = stateStore;
        _lifecycleService = lifecycleService;
        _logger = logger ?? NullLogger<AddonInventoryService>.Instance;
        _registry = registry;
    }

    public async Task<IReadOnlyList<AddonInventoryItem>> ScanAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        if (_registry == null)
        {
            throw new InvalidOperationException("Instance registry is not available.");
        }

        InstanceMetadata metadata = _registry.GetById(instanceId)
            ?? throw new InvalidOperationException("Instance metadata was not found.");
        string instanceRoot = _registry.GetPath(instanceId)
            ?? throw new InvalidOperationException("Instance path was not found.");

        return await ScanAsync(metadata, instanceRoot, cancellationToken);
    }

    public async Task<IReadOnlyList<AddonInventoryItem>> ScanAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(instanceRoot);
        AddonManifest manifest = await _manifestService.LoadManifestAsync(root);
        AddonStateDocument state = await _stateStore.LoadAsync(root, cancellationToken);
        bool serverRunning = _lifecycleService?.IsRunning(metadata.Id) == true;

        return await Task.Run(() =>
        {
            var items = new List<AddonInventoryItem>();
            ScanKind(root, metadata, AddonKind.Mod, manifest, state, serverRunning, items, cancellationToken);
            ScanKind(root, metadata, AddonKind.Plugin, manifest, state, serverRunning, items, cancellationToken);
            return (IReadOnlyList<AddonInventoryItem>)items
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.State)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    private void ScanKind(
        string root,
        InstanceMetadata metadata,
        AddonKind kind,
        AddonManifest manifest,
        AddonStateDocument state,
        bool serverRunning,
        List<AddonInventoryItem> items,
        CancellationToken cancellationToken)
    {
        string kindDirectoryName = AddonFileNamePolicy.KindDirectory(kind);
        string? enabledDirectory = PathSafety.ValidateContainedPath(root, kindDirectoryName);
        string? disabledDirectory = PathSafety.ValidateContainedPath(root, $"{kindDirectoryName}/.disabled");

        if (enabledDirectory != null && Directory.Exists(enabledDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(enabledDirectory, "*.jar", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(BuildItem(root, metadata, kind, AddonState.Enabled, file, manifest, state, serverRunning));
            }
        }

        if (disabledDirectory != null && Directory.Exists(disabledDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(disabledDirectory, "*.jar.disabled-by-pocketmc", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(BuildItem(root, metadata, kind, AddonState.Disabled, file, manifest, state, serverRunning));
            }
        }
    }

    private AddonInventoryItem BuildItem(
        string root,
        InstanceMetadata metadata,
        AddonKind kind,
        AddonState itemState,
        string fullPath,
        AddonManifest manifest,
        AddonStateDocument stateDocument,
        bool serverRunning)
    {
        string relativePath = ToRelativePath(root, fullPath);
        string actualFileName = Path.GetFileName(fullPath);
        string originalFileName = itemState == AddonState.Disabled && AddonFileNamePolicy.IsDisabledJarFileName(actualFileName)
            ? AddonFileNamePolicy.GetOriginalFileNameFromDisabled(actualFileName)
            : actualFileName;

        AddonStateEntry? stateEntry = AddonStateStore.FindByRelativePath(stateDocument, kind, relativePath);
        if (!string.IsNullOrWhiteSpace(stateEntry?.OriginalRelativePath))
        {
            originalFileName = Path.GetFileName(stateEntry.OriginalRelativePath);
        }

        JavaModMetadata jarMetadata = JavaModMetadataService.ScanJar(fullPath);
        AddonManifestEntry? manifestEntry = FindManifestEntry(manifest, originalFileName, actualFileName);
        AddonProvenance? provenance = BuildProvenance(manifestEntry, metadata);
        string displayName = ResolveDisplayName(jarMetadata, manifestEntry, stateEntry, originalFileName);
        ModSideSupport sideSupport = ResolveSideSupport(jarMetadata, manifestEntry);
        string sideLabel = ResolveSideLabel(sideSupport, jarMetadata.SideLabel);
        var warnings = new List<string>(jarMetadata.Warnings);

        FileInfo? fileInfo = TryGetFileInfo(fullPath);
        string disabledPath = itemState == AddonState.Disabled
            ? fullPath
            : GetPlannedDisabledPath(root, kind, originalFileName);

        return new AddonInventoryItem
        {
            InstanceId = metadata.Id,
            Kind = kind,
            State = itemState,
            DisplayName = displayName,
            FileName = originalFileName,
            RelativePath = relativePath,
            FullPath = fullPath,
            DisabledPath = disabledPath,
            LoaderType = jarMetadata.LoaderType,
            Version = jarMetadata.Version ?? stateEntry?.Version,
            ModId = string.IsNullOrWhiteSpace(jarMetadata.ModId) ? null : jarMetadata.ModId,
            SideSupport = sideSupport,
            SideLabel = sideLabel,
            IconBytes = jarMetadata.IconBytes,
            Dependencies = jarMetadata.Dependencies.ToArray(),
            Warnings = warnings,
            UpdateStatus = provenance == null ? AddonUpdateStatus.UnknownSource : AddonUpdateStatus.Unknown,
            CanEnable = itemState == AddonState.Disabled && !serverRunning,
            CanDisable = itemState == AddonState.Enabled && !serverRunning,
            RequiresServerStopped = true,
            Provenance = provenance,
            SizeBytes = fileInfo?.Length ?? 0,
            LastModifiedUtc = fileInfo?.LastWriteTimeUtc ?? DateTime.MinValue
        };
    }

    private static AddonManifestEntry? FindManifestEntry(
        AddonManifest manifest,
        string originalFileName,
        string actualFileName)
    {
        return manifest.Entries.FirstOrDefault(entry =>
            entry.FileName.Equals(originalFileName, StringComparison.OrdinalIgnoreCase) ||
            entry.FileName.Equals(actualFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static AddonProvenance? BuildProvenance(AddonManifestEntry? entry, InstanceMetadata metadata)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Provider))
        {
            return null;
        }

        if (entry.Provider.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
            entry.Provider.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new AddonProvenance
        {
            Provider = entry.Provider,
            ProjectId = entry.ProjectId,
            VersionId = entry.VersionId,
            InstalledFileName = entry.FileName,
            InstalledFileHash = entry.FileHash,
            InstalledFileHashType = entry.FileHashType,
            MinecraftVersion = entry.MinecraftVersion ?? metadata.MinecraftVersion,
            Loader = entry.Loader ?? metadata.Compatibility.LoaderName,
            InstalledAtUtc = entry.InstalledAt
        };
    }

    private static string ResolveDisplayName(
        JavaModMetadata jarMetadata,
        AddonManifestEntry? manifestEntry,
        AddonStateEntry? stateEntry,
        string originalFileName)
    {
        if (!string.IsNullOrWhiteSpace(jarMetadata.DisplayName) &&
            !jarMetadata.LoaderType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return jarMetadata.DisplayName;
        }

        return FirstNonEmpty(
            manifestEntry?.DisplayName,
            manifestEntry?.ProjectTitle,
            stateEntry?.LastKnownDisplayName,
            jarMetadata.DisplayName,
            Path.GetFileNameWithoutExtension(originalFileName));
    }

    private static ModSideSupport ResolveSideSupport(JavaModMetadata jarMetadata, AddonManifestEntry? manifestEntry)
    {
        if (manifestEntry != null &&
            (!string.IsNullOrWhiteSpace(manifestEntry.ClientSide) || !string.IsNullOrWhiteSpace(manifestEntry.ServerSide)))
        {
            ModSideSupport providerSide = MapProviderSide(manifestEntry.ClientSide, manifestEntry.ServerSide);
            if (providerSide != ModSideSupport.Unknown)
            {
                return providerSide;
            }
        }

        return jarMetadata.SideSupport;
    }

    private static ModSideSupport MapProviderSide(string? clientSide, string? serverSide)
    {
        string client = clientSide?.ToLowerInvariant() ?? "";
        string server = serverSide?.ToLowerInvariant() ?? "";

        bool clientSupported = client is "required" or "supported";
        bool serverSupported = server is "required" or "supported";

        if (clientSupported && serverSupported)
        {
            return ModSideSupport.ClientAndServer;
        }

        if (clientSupported && server == "unsupported")
        {
            return ModSideSupport.ClientOnly;
        }

        if (client == "unsupported" && serverSupported)
        {
            return ModSideSupport.ServerOnly;
        }

        if (server == "optional")
        {
            return ModSideSupport.OptionalOnServer;
        }

        if (client == "optional")
        {
            return ModSideSupport.OptionalOnClient;
        }

        return ModSideSupport.Unknown;
    }

    private static string ResolveSideLabel(ModSideSupport sideSupport, string metadataSideLabel)
    {
        return sideSupport switch
        {
            ModSideSupport.ClientOnly => "Client-only",
            ModSideSupport.ServerOnly => "Server-only",
            ModSideSupport.ClientAndServer => "Client + Server",
            ModSideSupport.OptionalOnServer => "Optional on server",
            ModSideSupport.OptionalOnClient => "Optional on client",
            _ => string.IsNullOrWhiteSpace(metadataSideLabel) || metadataSideLabel.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                ? "Side unknown"
                : metadataSideLabel
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "Unknown add-on";
    }

    private static FileInfo? TryGetFileInfo(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists ? info : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string GetPlannedDisabledPath(string root, AddonKind kind, string fileName)
    {
        string relativePath = $"{AddonFileNamePolicy.KindDirectory(kind)}/.disabled/{AddonFileNamePolicy.GetDisabledFileName(fileName)}";
        return PathSafety.ValidateContainedPath(root, relativePath) ?? "";
    }

    private static string ToRelativePath(string root, string fullPath)
    {
        string relative = Path.GetRelativePath(root, fullPath);
        return AddonFileNamePolicy.NormalizeRelativePath(relative);
    }
}
