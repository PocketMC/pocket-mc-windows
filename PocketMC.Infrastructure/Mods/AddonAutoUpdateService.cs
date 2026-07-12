using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Infrastructure.Mods;
using PocketMC.Infrastructure;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Mods;
using PocketMC.Application.Interfaces.Mods;

namespace PocketMC.Infrastructure.Telemetry;

/// <summary>
/// Orchestrates automatic addon update checks and installation at server startup.
/// Shows the update selection dialog when updates are available.
/// Only supports engines with marketplace integration (excludes Forge/NeoForge).
/// </summary>
public sealed class AddonAutoUpdateService
{
    private readonly AddonUpdateCheckService _checkService;
    private readonly AddonUpdateService _updateService;
    private readonly AddonManifestService _manifestService;
    private readonly AddonInventoryService _inventoryService;
    private readonly IAddonAutoUpdateDialog _dialog;
    private readonly ILogger<AddonAutoUpdateService> _logger;

    public AddonAutoUpdateService(
        AddonUpdateCheckService checkService,
        AddonUpdateService updateService,
        AddonManifestService manifestService,
        AddonInventoryService inventoryService,
        IAddonAutoUpdateDialog dialog,
        ILogger<AddonAutoUpdateService> logger)
    {
        _checkService = checkService;
        _updateService = updateService;
        _manifestService = manifestService;
        _inventoryService = inventoryService;
        _dialog = dialog;
        _logger = logger;
    }

    /// <summary>
    /// Checks all marketplace-tracked addons for updates and, if any are found,
    /// shows the update selection dialog on the UI thread.
    /// Returns true if any updates were installed, false otherwise.
    /// </summary>
    /// <param name="metadata">Instance metadata (must have SupportsModrinth == true).</param>
    /// <param name="serverDir">Instance server directory.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task<bool> CheckAndPromptUpdatesAsync(
        InstanceMetadata metadata,
        string serverDir,
        CancellationToken cancellationToken = default)
    {
        // Safety: never run for engines without marketplace support (Forge, NeoForge)
        if (!metadata.Compatibility.SupportsModrinth)
        {
            _logger.LogDebug("Auto-update skipped for {ServerType}: marketplace not supported.", metadata.ServerType);
            return false;
        }

        _logger.LogInformation("Auto-update: checking addons for '{InstanceName}'...", metadata.Name);

        var manifest = await _manifestService.LoadManifestAsync(serverDir);
        if (manifest == null || manifest.Entries.Count == 0)
        {
            _logger.LogDebug("Auto-update: no manifest entries found for '{InstanceName}'.", metadata.Name);
            return false;
        }

        var inventory = await _inventoryService.ScanAsync(metadata, serverDir, cancellationToken);
        if (inventory.Count == 0)
        {
            _logger.LogDebug("Auto-update: no installed addons found for '{InstanceName}'.", metadata.Name);
            return false;
        }

        // Check each tracked addon for updates
        var updatable = new List<AutoUpdateCandidate>();

        foreach (var entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matchingAddon = inventory.FirstOrDefault(a =>
                string.Equals(a.FileName, entry.FileName, StringComparison.OrdinalIgnoreCase));

            if (matchingAddon == null) continue;

            try
            {
                var result = await _checkService.CheckAsync(
                    metadata,
                    serverDir,
                    matchingAddon,
                    cancellationToken);

                if (result.Status == AddonUpdateStatus.UpdateAvailable && result.UpdateInfo != null)
                {
                    updatable.Add(new AutoUpdateCandidate
                    {
                        DisplayName = result.UpdateInfo.ProjectTitle ?? matchingAddon.DisplayName,
                        InstalledVersion = matchingAddon.Version ?? "unknown",
                        LatestVersion = result.UpdateInfo.LatestVersionName ?? result.UpdateInfo.LatestVersionId ?? "new version",
                        UpdateInfo = result.UpdateInfo,
                        OldFileName = matchingAddon.FileName,
                        Provider = entry.Provider,
                        ProjectId = entry.ProjectId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-update: check failed for '{Provider}/{ProjectId}'.", entry.Provider, entry.ProjectId);
            }
        }

        if (updatable.Count == 0)
        {
            _logger.LogInformation("Auto-update: all addons are up to date for '{InstanceName}'.", metadata.Name);
            return false;
        }

        _logger.LogInformation("Auto-update: {Count} update(s) available for '{InstanceName}'.", updatable.Count, metadata.Name);

        // Delegate to the UI dialog via interface
        var dialogItems = updatable.Select(c => new AutoUpdateDialogItem(
            c.DisplayName,
            c.InstalledVersion,
            c.LatestVersion,
            new AddonUpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersionId = c.UpdateInfo.LatestVersionId,
                LatestVersionName = c.UpdateInfo.LatestVersionName,
                LatestFileName = c.UpdateInfo.LatestFileName,
                LatestDownloadUrl = c.UpdateInfo.LatestDownloadUrl,
                ProjectTitle = c.UpdateInfo.ProjectTitle,
                Hash = c.UpdateInfo.Hash,
                HashType = c.UpdateInfo.HashType,
                ReleaseType = c.UpdateInfo.ReleaseType,
                Warnings = c.UpdateInfo.Warnings?.ToList() ?? new List<string>()
            },
            c.OldFileName,
            c.Provider,
            c.ProjectId)).ToList();

        bool anyInstalled = await _dialog.ShowAutoUpdateDialogAsync(
            dialogItems,
            async (item, progress, ct) =>
            {
                await _updateService.ApplyUpdateAsync(
                    serverDir,
                    item.OldFileName,
                    item.UpdateInfo,
                    item.Provider,
                    item.ProjectId,
                    metadata.Compatibility,
                    progress,
                    ct);
            });

        return anyInstalled;
    }

    private static string GetLoaderType(AddonInventoryItem addon, InstanceMetadata metadata)
    {
        // Use the addon's own loader type if available, otherwise derive from engine
        if (!string.IsNullOrEmpty(addon.LoaderType) && addon.LoaderType != "Unknown")
            return addon.LoaderType;

        var compat = metadata.Compatibility;
        return compat.LoaderName ?? metadata.ServerType ?? "unknown";
    }

    private sealed class AutoUpdateCandidate
    {
        public string DisplayName { get; init; } = "";
        public string InstalledVersion { get; init; } = "unknown";
        public string LatestVersion { get; init; } = "new version";
        public AddonUpdateInfo UpdateInfo { get; init; } = null!;
        public string OldFileName { get; init; } = "";
        public string Provider { get; init; } = "";
        public string ProjectId { get; init; } = "";
    }
}


