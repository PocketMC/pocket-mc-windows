using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Application.Services.Mods;
using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces.Mods;

/// <summary>
/// Represents an update candidate shown in the auto-update UI.
/// </summary>
public sealed record AutoUpdateDialogItem(
    string DisplayName,
    string InstalledVersion,
    string LatestVersion,
    AddonUpdateCheckResult UpdateInfo,
    string OldFileName,
    string Provider,
    string ProjectId);

/// <summary>
/// Abstracts the auto-update dialog interaction so Infrastructure doesn't depend on WPF UI.
/// </summary>
public interface IAddonAutoUpdateDialog
{
    /// <summary>
    /// Shows the auto-update dialog with the given update candidates.
    /// Returns true if any updates were installed.
    /// </summary>
    Task<bool> ShowAutoUpdateDialogAsync(
        IReadOnlyList<AutoUpdateDialogItem> items,
        Func<AutoUpdateDialogItem, IProgress<DownloadProgress>, CancellationToken, Task> installAction);
}
