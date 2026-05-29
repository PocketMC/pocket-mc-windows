using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Mods;

public sealed class AddonToggleService
{
    private readonly AddonStateStore _stateStore;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly ILogger<AddonToggleService> _logger;
    private readonly AddonManifestService? _manifestService;

    public AddonToggleService(
        AddonStateStore stateStore,
        IServerLifecycleService lifecycleService,
        ILogger<AddonToggleService>? logger = null,
        AddonManifestService? manifestService = null)
    {
        _stateStore = stateStore;
        _lifecycleService = lifecycleService;
        _logger = logger ?? NullLogger<AddonToggleService>.Instance;
        _manifestService = manifestService;
    }

    public async Task<AddonToggleResult> DisableAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        AddonKind kind,
        string relativePath,
        AddonDisabledBySource disabledBy = AddonDisabledBySource.User,
        string? disabledReason = null,
        CancellationToken cancellationToken = default)
    {
        if (_lifecycleService.IsRunning(metadata.Id))
        {
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.ServerRunning,
                "Stop the server before enabling or disabling mods/plugins.");
        }

        TogglePathResolution resolution = ResolveEnabledPath(instanceRoot, kind, relativePath);
        if (!resolution.Success)
        {
            return AddonToggleResult.Fail(AddonToggleErrorCodes.InvalidPath, "This add-on path is not valid.");
        }

        if (!File.Exists(resolution.FullPath))
        {
            return AddonToggleResult.Fail(AddonToggleErrorCodes.NotFound, "This add-on file no longer exists.");
        }

        IAddonMetadataScanner scanner = AddonMetadataScannerFactory.GetScanner(metadata.Compatibility);
        JavaModMetadata metadataSnapshot = scanner.Scan(resolution.FullPath);
        AddonStateDocument state = await _stateStore.LoadAsync(instanceRoot, cancellationToken);
        AddonStateEntry? entry = AddonStateStore.FindByRelativePath(state, kind, resolution.RelativePath);

        string disabledDirectory = EnsureDisabledDirectory(instanceRoot, kind);
        string disabledFileName = AddonFileNamePolicy.GetUniqueDisabledFileName(resolution.FileName, disabledDirectory);
        string disabledRelativePath = $"{AddonFileNamePolicy.KindDirectory(kind)}/.disabled/{disabledFileName}";
        string? disabledFullPath = PathSafety.ValidateContainedPath(instanceRoot, disabledRelativePath);
        if (disabledFullPath == null)
        {
            return AddonToggleResult.Fail(AddonToggleErrorCodes.InvalidPath, "The disabled add-on path is not valid.");
        }

        AddonProvenance? provenance = await TryGetProvenanceAsync(instanceRoot, resolution.FileName, metadata, cancellationToken);

        try
        {
            await Task.Run(() => File.Move(resolution.FullPath, disabledFullPath, overwrite: false), cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to disable add-on {RelativePath}.", resolution.RelativePath);
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.FileLocked,
                "Could not move the add-on because the file is in use. Stop anything using it and try again.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied while disabling add-on {RelativePath}.", resolution.RelativePath);
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.FileLocked,
                "Could not move the add-on because Windows denied access. Stop anything using it and try again.");
        }

        entry ??= new AddonStateEntry
        {
            StableItemId = AddonStateStore.CreateStableItemId(kind, resolution.RelativePath),
            Kind = kind,
            OriginalRelativePath = resolution.RelativePath
        };

        entry.DisabledRelativePath = AddonFileNamePolicy.NormalizeRelativePath(disabledRelativePath);
        // For non-Java engines (Bedrock, Pocketmine) the passthrough scanner
        // returns minimal info.  Prefer the existing state entry values so we
        // do not wipe previously-known metadata to "Unknown" / null.
        bool scannerHasRichMetadata = !metadataSnapshot.LoaderType.Equals("Native", StringComparison.OrdinalIgnoreCase);
        entry.LastKnownDisplayName = scannerHasRichMetadata
            ? metadataSnapshot.DisplayName
            : (!string.IsNullOrWhiteSpace(entry.LastKnownDisplayName) ? entry.LastKnownDisplayName : metadataSnapshot.DisplayName);
        entry.LoaderType = scannerHasRichMetadata
            ? metadataSnapshot.LoaderType
            : (!string.IsNullOrWhiteSpace(entry.LoaderType) && !entry.LoaderType.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? entry.LoaderType : metadataSnapshot.LoaderType);
        entry.Version = scannerHasRichMetadata
            ? metadataSnapshot.Version
            : (entry.Version ?? metadataSnapshot.Version);
        entry.Provenance = provenance ?? entry.Provenance;
        entry.LastToggledUtc = DateTime.UtcNow;
        entry.DisabledReason = disabledReason;
        entry.DisabledBy = disabledBy;

        if (!state.Entries.Contains(entry))
        {
            state.Entries.Add(entry);
        }

        await _stateStore.SaveAsync(instanceRoot, state, cancellationToken);
        return AddonToggleResult.Ok("Add-on disabled.");
    }

    public async Task<AddonToggleResult> EnableAsync(
        InstanceMetadata metadata,
        string instanceRoot,
        AddonKind kind,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (_lifecycleService.IsRunning(metadata.Id))
        {
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.ServerRunning,
                "Stop the server before enabling or disabling mods/plugins.");
        }

        TogglePathResolution resolution = ResolveDisabledPath(instanceRoot, kind, relativePath);
        if (!resolution.Success)
        {
            return AddonToggleResult.Fail(AddonToggleErrorCodes.InvalidPath, "This disabled add-on path is not valid.");
        }

        if (!File.Exists(resolution.FullPath))
        {
            return AddonToggleResult.Fail(AddonToggleErrorCodes.NotFound, "This disabled add-on file no longer exists.");
        }

        AddonStateDocument state = await _stateStore.LoadAsync(instanceRoot, cancellationToken);
        AddonStateEntry? entry = AddonStateStore.FindByRelativePath(state, kind, resolution.RelativePath);
        string originalRelativePath = entry?.OriginalRelativePath
            ?? $"{AddonFileNamePolicy.KindDirectory(kind)}/{AddonFileNamePolicy.GetOriginalFileNameFromDisabled(resolution.FileName)}";
        originalRelativePath = AddonFileNamePolicy.NormalizeRelativePath(originalRelativePath);

        TogglePathResolution target = ResolveEnabledPath(instanceRoot, kind, originalRelativePath, requireFileExists: false);
        if (!target.Success)
        {
            return AddonToggleResult.Fail(AddonToggleErrorCodes.InvalidPath, "The original add-on path is not valid.");
        }

        if (File.Exists(target.FullPath))
        {
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.TargetExists,
                "Cannot enable this add-on because a file with the original name already exists.");
        }

        try
        {
            await Task.Run(() => File.Move(resolution.FullPath, target.FullPath, overwrite: false), cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to enable add-on {RelativePath}.", resolution.RelativePath);
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.FileLocked,
                "Could not move the add-on because the file is in use. Stop anything using it and try again.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied while enabling add-on {RelativePath}.", resolution.RelativePath);
            return AddonToggleResult.Fail(
                AddonToggleErrorCodes.FileLocked,
                "Could not move the add-on because Windows denied access. Stop anything using it and try again.");
        }

        if (entry != null)
        {
            entry.DisabledRelativePath = null;
            entry.LastToggledUtc = DateTime.UtcNow;
            entry.DisabledReason = null;
            entry.DisabledBy = AddonDisabledBySource.Unknown;
            await _stateStore.SaveAsync(instanceRoot, state, cancellationToken);
        }

        return AddonToggleResult.Ok("Add-on enabled.");
    }

    private static TogglePathResolution ResolveEnabledPath(
        string instanceRoot,
        AddonKind kind,
        string relativePath,
        bool requireFileExists = true)
    {
        string normalized = AddonFileNamePolicy.NormalizeRelativePath(relativePath);
        string kindDirectory = AddonFileNamePolicy.KindDirectory(kind);
        if (!normalized.StartsWith($"{kindDirectory}/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.disabled/", StringComparison.OrdinalIgnoreCase))
        {
            return TogglePathResolution.Invalid;
        }

        string fileName = Path.GetFileName(normalized);
        if (!AddonFileNamePolicy.IsEnabledJarFileName(fileName))
        {
            return TogglePathResolution.Invalid;
        }

        string? fullPath = PathSafety.ValidateContainedPath(instanceRoot, normalized);
        if (fullPath == null)
        {
            return TogglePathResolution.Invalid;
        }

        if (requireFileExists && !File.Exists(fullPath))
        {
            return new TogglePathResolution(true, fullPath, normalized, fileName);
        }

        return new TogglePathResolution(true, fullPath, normalized, fileName);
    }

    private static TogglePathResolution ResolveDisabledPath(
        string instanceRoot,
        AddonKind kind,
        string relativePath)
    {
        string normalized = AddonFileNamePolicy.NormalizeRelativePath(relativePath);
        string kindDirectory = AddonFileNamePolicy.KindDirectory(kind);
        if (!normalized.StartsWith($"{kindDirectory}/.disabled/", StringComparison.OrdinalIgnoreCase))
        {
            return TogglePathResolution.Invalid;
        }

        string fileName = Path.GetFileName(normalized);
        if (!AddonFileNamePolicy.IsDisabledJarFileName(fileName))
        {
            return TogglePathResolution.Invalid;
        }

        string? fullPath = PathSafety.ValidateContainedPath(instanceRoot, normalized);
        if (fullPath == null)
        {
            return TogglePathResolution.Invalid;
        }

        return new TogglePathResolution(true, fullPath, normalized, fileName);
    }

    private static string EnsureDisabledDirectory(string instanceRoot, AddonKind kind)
    {
        string relativeDirectory = $"{AddonFileNamePolicy.KindDirectory(kind)}/.disabled";
        string disabledDirectory = PathSafety.ValidateContainedPath(instanceRoot, relativeDirectory)
            ?? throw new InvalidOperationException("Invalid disabled add-on directory.");
        Directory.CreateDirectory(disabledDirectory);
        return disabledDirectory;
    }

    private async Task<AddonProvenance?> TryGetProvenanceAsync(
        string instanceRoot,
        string fileName,
        InstanceMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (_manifestService == null)
        {
            return null;
        }

        AddonManifest manifest = await _manifestService.LoadManifestAsync(instanceRoot);
        AddonManifestEntry? entry = manifest.Entries.FirstOrDefault(e =>
            e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new AddonProvenance
        {
            Provider = string.IsNullOrWhiteSpace(entry.Provider) ? "Unknown" : entry.Provider,
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

    private readonly record struct TogglePathResolution(
        bool Success,
        string FullPath,
        string RelativePath,
        string FileName)
    {
        public static TogglePathResolution Invalid => new(false, "", "", "");
    }
}
