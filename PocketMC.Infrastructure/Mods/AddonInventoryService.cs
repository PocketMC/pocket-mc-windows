using PocketMC.Application.Services.Mods;
using PocketMC.Application.Services.Instances;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Application.Interfaces;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Domain.Security;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Mods;

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
            
            var itemsToKeep = new List<AddonInventoryItem>();
            
            if (metadata.IsModpack)
            {
                itemsToKeep.AddRange(items);
            }
            else
            {
                foreach (var kindGroup in items.GroupBy(i => i.Kind))
                {
                    var groupedById = kindGroup.GroupBy(i => !string.IsNullOrEmpty(i.ModId) ? i.ModId.ToLowerInvariant() : i.DisplayName.ToLowerInvariant());
                    
                    foreach (var group in groupedById)
                    {
                        if (group.Count() == 1)
                        {
                            itemsToKeep.Add(group.First());
                            continue;
                        }
                        
                        var sorted = group
                            .OrderBy(i => i.Provenance == null ? 1 : 0) // Prefer tracked over manual
                            .ThenByDescending(i => i.LastModifiedUtc) // Prefer newest file
                            .ToList();
                            
                        var winner = sorted.First();
                        itemsToKeep.Add(winner);
                        
                        foreach (var loser in sorted.Skip(1))
                        {
                            try
                            {
                                if (File.Exists(loser.FullPath)) File.Delete(loser.FullPath);
                                if (!string.IsNullOrEmpty(loser.DisabledPath) && File.Exists(loser.DisabledPath)) File.Delete(loser.DisabledPath);
                            }
                            catch { /* Ignore locks */ }
                        }
                    }
                }
            }

            return (IReadOnlyList<AddonInventoryItem>)itemsToKeep
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
                var item = BuildItem(root, metadata, kind, AddonState.Enabled, file, manifest, state, serverRunning);
                if (item != null) items.Add(item);
            }
        }

        if (disabledDirectory != null && Directory.Exists(disabledDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(disabledDirectory, "*.jar.disabled-by-pocketmc", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = BuildItem(root, metadata, kind, AddonState.Disabled, file, manifest, state, serverRunning);
                if (item != null) items.Add(item);
            }
        }
    }

    private AddonInventoryItem? BuildItem(
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

        // Treat hybrid JARs (e.g. ViaVersion) as plugins if the server supports plugins.
        if (jarMetadata.HasPluginMetadata && metadata.Compatibility.SupportsPlugins)
        {
            jarMetadata.LoaderType = "Plugin";
        }
        AddonManifestEntry? manifestEntry = FindManifestEntry(manifest, originalFileName, actualFileName);

        // Determine compatibility
        bool isCompatibleLoader = true;
        if (jarMetadata.LoaderType != "Unknown")
        {
            if (jarMetadata.LoaderType.Equals("Plugin", StringComparison.OrdinalIgnoreCase))
            {
                isCompatibleLoader = metadata.Compatibility.SupportsPlugins;
            }
            else
            {
                isCompatibleLoader = metadata.Compatibility.CompatibleLoaderNames.Contains(jarMetadata.LoaderType, StringComparer.OrdinalIgnoreCase);
            }
        }

        ModSideSupport sideSupport = ResolveSideSupport(jarMetadata, manifestEntry);

        bool isMinecraftIncompatible = false;
        if (!string.IsNullOrWhiteSpace(jarMetadata.RequiredMinecraftVersion) && !string.IsNullOrWhiteSpace(metadata.MinecraftVersion))
        {
            isMinecraftIncompatible = !SemanticVersionHelper.IsCompatible(jarMetadata.RequiredMinecraftVersion, metadata.MinecraftVersion);
        }

        bool isLoaderVersionIncompatible = false;
        if (!string.IsNullOrWhiteSpace(jarMetadata.RequiredLoaderVersion) && !string.IsNullOrWhiteSpace(metadata.LoaderVersion))
        {
            isLoaderVersionIncompatible = !SemanticVersionHelper.IsCompatible(jarMetadata.RequiredLoaderVersion, metadata.LoaderVersion);
        }

        bool isInvalid = jarMetadata.LoaderType == "Unknown" ||
                         jarMetadata.IsPluginInModsFolder ||
                         sideSupport == ModSideSupport.ClientOnly ||
                         !isCompatibleLoader ||
                         isMinecraftIncompatible ||
                         isLoaderVersionIncompatible;

        if (metadata.IsModpack)
        {
            isInvalid = false;
        }

        if (isInvalid)
        {
            try
            {
                File.Delete(fullPath);
                // Also clean up any disabled version if it exists
                if (itemState == AddonState.Disabled)
                {
                    File.Delete(fullPath); 
                }
            }
            catch { /* Ignore deletion errors if locked */ }
            
            return null; // Don't return an item so it doesn't show in UI
        }

        AddonProvenance? provenance = BuildProvenance(manifestEntry, metadata);
        string displayName = ResolveDisplayName(jarMetadata, manifestEntry, stateEntry, originalFileName);
        string sideLabel = ResolveSideLabel(sideSupport, jarMetadata.SideLabel);

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
