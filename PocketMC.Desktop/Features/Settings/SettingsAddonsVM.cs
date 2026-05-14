using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Features.Mods;
using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsAddonsVM : ViewModelBase
    {
        private readonly InstanceMetadata _metadata;
        private string _serverDir;

        public void UpdateServerDir(string newDir) => _serverDir = newDir;
        private readonly ModpackService _modpackService;
        private readonly BedrockAddonInstaller _bedrockInstaller;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<bool> _isRunningCheck;
        private readonly Action _onAddonChanged;
        private readonly AddonManifestService _manifestService;
        private readonly AddonUpdateService _updateService;

        // ── Installed addon collections ──────────────────────────────────
        public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
        public ObservableCollection<ModItemViewModel> Mods { get; } = new();

        // ── Engine predicates ────────────────────────────────────────────
        public bool ShowVanillaWarning   => _metadata.ServerType?.StartsWith("Vanilla",    StringComparison.OrdinalIgnoreCase) == true;
        public bool IsBedrockDedicated  => _metadata.Compatibility.Family == EngineFamily.Bedrock;
        public bool IsPocketmine        => _metadata.Compatibility.Family == EngineFamily.Pocketmine;
        public bool IsBedrockOrPocketmine => IsBedrockDedicated || IsPocketmine;
        /// <summary>True for Java-based engines (Vanilla, Paper, Fabric, Forge, NeoForge).</summary>
        public bool IsJavaEngine => _metadata.Compatibility.IsJavaEngine;

        public bool SupportsPlugins => _metadata.Compatibility.SupportsPlugins;
        public bool SupportsMods => _metadata.Compatibility.SupportsMods;
        public bool SupportsBedrockAddons => _metadata.Compatibility.SupportsBedrockAddons;

        // ── Commands ─────────────────────────────────────────────────────
        // Shared / Java
        public ICommand AddPluginCommand          { get; }
        public ICommand DeletePluginCommand       { get; }
        public ICommand BrowseModrinthPluginsCommand { get; }
        public ICommand AddModCommand             { get; }
        public ICommand DeleteModCommand          { get; }
        public ICommand BrowseModrinthModsCommand { get; }
        public ICommand ImportModpackCommand      { get; }
        public ICommand BrowseModpacksCommand     { get; }

        // Bedrock-specific
        public ICommand ImportBedrockAddonCommand { get; }
        public ICommand DeleteBedrockAddonCommand { get; }

        // PocketMine-specific
        public ICommand BrowsePoggitCommand { get; }

        // Update commands
        public ICommand UpdatePluginCommand { get; }
        public ICommand UpdateModCommand { get; }
        public ICommand UpdateAllPluginsCommand { get; }
        public ICommand UpdateAllModsCommand { get; }

        // Update All state
        private bool _isUpdatingAll;
        public bool IsUpdatingAll
        {
            get => _isUpdatingAll;
            set => SetProperty(ref _isUpdatingAll, value);
        }

        private string _updateAllStatusText = "";
        public string UpdateAllStatusText
        {
            get => _updateAllStatusText;
            set => SetProperty(ref _updateAllStatusText, value);
        }

        public SettingsAddonsVM(
            InstanceMetadata metadata,
            string serverDir,
            ModpackService modpackService,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            Func<bool> isRunningCheck,
            Action onAddonChanged)
        {
            _metadata          = metadata;
            _serverDir         = serverDir;
            _modpackService    = modpackService;
            _dialogService     = dialogService;
            _navigationService = navigationService;
            _serviceProvider   = serviceProvider;
            _isRunningCheck    = isRunningCheck;
            _onAddonChanged    = onAddonChanged;
            _manifestService   = serviceProvider.GetRequiredService<AddonManifestService>();
            _updateService     = serviceProvider.GetRequiredService<AddonUpdateService>();

            // Resolve the Bedrock installer from DI (if not Bedrock this is a no-op).
            _bedrockInstaller = serviceProvider.GetRequiredService<BedrockAddonInstaller>();

            // ── Plugin commands — routed by engine ───────────────────────────────────
            // BDS: no plugins concept (addons handled via Mods section below)
            // PocketMine: .phar files via Poggit browser
            // Java: JAR files via local picker
            AddPluginCommand            = new RelayCommand(
                async _ => await AddPluginAsync(),
                _ => !_isRunningCheck() && !ShowVanillaWarning && _metadata.Compatibility.SupportsPlugins);
            DeletePluginCommand         = new RelayCommand(
                async p => await DeletePluginAsync(p as string),
                _ => !_isRunningCheck() && _metadata.Compatibility.SupportsPlugins);
            BrowseModrinthPluginsCommand = new RelayCommand(
                _ => { if (IsPocketmine) BrowsePoggit(); else BrowseModrinth("project_type:plugin"); },
                _ => _metadata.Compatibility.SupportsPlugins && (_metadata.Compatibility.SupportsModrinth || IsPocketmine));

            // ── Mod commands — routed by engine ──────────────────────────────────────
            // BDS: "Add Mod" triggers local .mcpack/.mcaddon import
            // Java: JAR picker
            AddModCommand               = new RelayCommand(
                async _ => { if (IsBedrockDedicated) await ImportBedrockAddonAsync(); else await AddModAsync(); },
                _ => !_isRunningCheck() && !ShowVanillaWarning && (_metadata.Compatibility.SupportsMods || _metadata.Compatibility.SupportsBedrockAddons));
            DeleteModCommand            = new RelayCommand(
                async p => { if (IsBedrockDedicated) await DeleteBedrockAddonAsync(p as string); else await DeleteModAsync(p as string); },
                _ => !_isRunningCheck());
            BrowseModrinthModsCommand   = new RelayCommand(
                _ => { if (IsBedrockDedicated) ImportBedrockAddonCommand?.Execute(null); else BrowseModrinth("project_type:mod"); },
                _ => _metadata.Compatibility.SupportsMods && _metadata.Compatibility.SupportsModrinth);
            ImportModpackCommand        = new RelayCommand(async _ => await ImportModpackAsync(), _ => _metadata.Compatibility.SupportsModpacks);
            BrowseModpacksCommand       = new RelayCommand(_ => BrowseModrinth("project_type:modpack"), _ => _metadata.Compatibility.SupportsModpacks);

            // ── Bedrock-specific commands (also reachable via unified commands above) ─
            ImportBedrockAddonCommand   = new RelayCommand(async _ => await ImportBedrockAddonAsync(), _ => IsBedrockDedicated && !_isRunningCheck());
            DeleteBedrockAddonCommand   = new RelayCommand(async p => await DeleteBedrockAddonAsync(p as string), _ => IsBedrockDedicated && !_isRunningCheck());

            // ── PocketMine-specific commands ──────────────────────────────
            BrowsePoggitCommand         = new RelayCommand(_ => BrowsePoggit(), _ => IsPocketmine);

            // ── Update commands ──────────────────────────────────────────────
            UpdatePluginCommand = new RelayCommand(
                async p => await UpdateAddonAsync(p as PluginItemViewModel),
                _ => !_isRunningCheck() && !_isUpdatingAll);
            UpdateModCommand = new RelayCommand(
                async p => await UpdateAddonAsync(p as ModItemViewModel),
                _ => !_isRunningCheck() && !_isUpdatingAll);
            UpdateAllPluginsCommand = new RelayCommand(
                async _ => await UpdateAllAddonsAsync(isPlugins: true),
                _ => !_isRunningCheck() && !_isUpdatingAll && Plugins.Any(p => p.IsTracked));
            UpdateAllModsCommand = new RelayCommand(
                async _ => await UpdateAllAddonsAsync(isPlugins: false),
                _ => !_isRunningCheck() && !_isUpdatingAll && Mods.Any(m => m.IsTracked));
        }

        public void LoadAddons()
        {
            // Fire-and-forget: heavy I/O (manifest, file scan, JAR introspection)
            // runs on a thread-pool thread; only collection updates touch the UI thread.
            _ = Task.Run(() =>
            {
                var manifest = _manifestService.LoadManifest(_serverDir);

                if (IsBedrockDedicated)
                {
                    var items = BuildBedrockAddonList();
                    DispatchToUI(() => { Mods.Clear(); foreach (var m in items) Mods.Add(m); });
                }
                else if (IsPocketmine)
                {
                    var items = BuildPocketminePluginList(manifest);
                    DispatchToUI(() => { Plugins.Clear(); foreach (var p in items) Plugins.Add(p); });
                }
                else
                {
                    var pluginItems = BuildJavaPluginList(manifest);
                    var modItems = BuildJavaModList(manifest);
                    DispatchToUI(() =>
                    {
                        Plugins.Clear(); foreach (var p in pluginItems) Plugins.Add(p);
                        Mods.Clear();    foreach (var m in modItems) Mods.Add(m);
                    });
                }
            });
        }

        private static void DispatchToUI(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        // ── Bedrock addon management ──────────────────────────────────────

        private List<ModItemViewModel> BuildBedrockAddonList()
        {
            var result = new List<ModItemViewModel>();
            var installed = _bedrockInstaller.GetInstalledAddons(_serverDir);
            foreach (var addon in installed)
            {
                result.Add(new ModItemViewModel
                {
                    Name         = addon.Name,
                    Path         = addon.FilePath,
                    SizeKb       = addon.SizeKb,
                    LastModified = addon.LastModified,
                    AddonType    = addon.AddonType
                });
            }
            return result;
        }

        private async Task ImportBedrockAddonAsync()
        {
            const string filter = "Bedrock Add-ons (*.mcpack;*.mcaddon)|*.mcpack;*.mcaddon|All Files (*.*)|*.*";
            var files = await _dialogService.OpenFilesDialogAsync("Import Bedrock Add-on(s)", filter);

            foreach (var f in files)
            {
                try
                {
                    await _bedrockInstaller.InstallAsync(f, _serverDir);
                    _dialogService.ShowMessage("Installed", $"'{System.IO.Path.GetFileName(f)}' was installed successfully.");
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Install Failed", ex.Message, DialogType.Error);
                }
            }

            LoadAddons();
            _onAddonChanged();
        }

        private async Task DeleteBedrockAddonAsync(string? packDirOrId)
        {
            if (packDirOrId == null) return;

            string displayName = System.IO.Path.GetFileName(packDirOrId);
            if (await _dialogService.ShowDialogAsync("Confirm", $"Remove addon '{displayName}'?", DialogType.Question) != DialogResult.Yes)
                return;

            try
            {
                // UninstallAsync accepts a directory path — pass relative dir name if full path given.
                string id = System.IO.Path.GetFileName(packDirOrId);
                await _bedrockInstaller.UninstallAsync(id, _serverDir);
                LoadAddons();
                _onAddonChanged();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", ex.Message, DialogType.Error);
            }
        }

        // ── PocketMine plugin management ──────────────────────────────────

        private List<PluginItemViewModel> BuildPocketminePluginList(AddonManifest manifest)
        {
            var result = new List<PluginItemViewModel>();
            var dir = System.IO.Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.phar"))
            {
                var fi = new FileInfo(file);
                var entry = manifest.Entries.FirstOrDefault(e =>
                    e.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                result.Add(new PluginItemViewModel
                {
                    Name         = fi.Name,
                    Path         = file,
                    ApiVersion   = "PocketMine",
                    SizeKb       = fi.Length / 1024.0,
                    IsMismatch   = false,
                    LastModified = fi.LastWriteTime,
                    ManifestEntry = entry
                });
            }
            return result;
        }

        private void BrowsePoggit()
        {
            // Open the browser page locked to Poggit as the sole source.
            BrowseModrinthInternal("project_type:plugin", lockToPoggit: true);
        }

        // ── Java plugin / mod management ──────────────────────────────────

        private List<PluginItemViewModel> BuildJavaPluginList(AddonManifest manifest)
        {
            var result = new List<PluginItemViewModel>();
            var dir = System.IO.Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(dir)) return result;

#pragma warning disable CS0618 // PluginScanner is deprecated — retained for Java back-compat only
            foreach (var file in Directory.GetFiles(dir, "*.jar"))
            {
                var fi      = new FileInfo(file);
                string api  = PluginScanner.TryGetApiVersion(file) ?? "Unknown";
                string name = PluginScanner.TryGetPluginName(file) ?? fi.Name;
                bool bad    = PluginScanner.IsIncompatible(api == "Unknown" ? null : api, _metadata.MinecraftVersion);
                var entry = manifest.Entries.FirstOrDefault(e =>
                    e.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                result.Add(new PluginItemViewModel
                {
                    Name = name, Path = file, ApiVersion = api,
                    SizeKb = fi.Length / 1024.0, IsMismatch = bad,
                    LastModified = fi.LastWriteTime,
                    ManifestEntry = entry
                });
            }
#pragma warning restore CS0618
            return result;
        }

        private async Task AddPluginAsync()
        {
            string filter = IsPocketmine ? "PHAR Files (*.phar)|*.phar" : "JAR Files (*.jar)|*.jar";
            var files = await _dialogService.OpenFilesDialogAsync("Select Plugin(s)", filter);
            foreach (var f in files)
            {
                var dir = System.IO.Path.Combine(_serverDir, "plugins");
                Directory.CreateDirectory(dir);
                await FileUtils.CopyFileAsync(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)), true);
            }
            LoadAddons(); _onAddonChanged();
        }

        private async Task DeletePluginAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {System.IO.Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try 
                { 
                    await FileUtils.DeleteFileAsync(path); 
                    await _manifestService.UnregisterByFileNameAsync(_serverDir, Path.GetFileName(path));
                    LoadAddons(); 
                    _onAddonChanged(); 
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private List<ModItemViewModel> BuildJavaModList(AddonManifest manifest)
        {
            var result = new List<ModItemViewModel>();
            var dir = System.IO.Path.Combine(_serverDir, "mods");
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.jar"))
            {
                var fi = new FileInfo(file);
                var entry = manifest.Entries.FirstOrDefault(e =>
                    e.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                result.Add(new ModItemViewModel
                {
                    Name = fi.Name, Path = file,
                    SizeKb = fi.Length / 1024.0,
                    LastModified = fi.LastWriteTime,
                    ManifestEntry = entry
                });
            }
            return result;
        }

        private async Task AddModAsync()
        {
            var files = await _dialogService.OpenFilesDialogAsync("Select Mod(s)", "JAR Files (*.jar)|*.jar");
            foreach (var f in files)
            {
                var fileName = System.IO.Path.GetFileName(f).ToLowerInvariant();
                
                // Audit for common client-side only mods that crash servers
                if (fileName.Contains("sodium") || fileName.Contains("iris") || fileName.Contains("canvas") || fileName.Contains("optifine"))
                {
                    var res = await _dialogService.ShowDialogAsync("Client-Side Mod Warning", 
                        $"The mod '{System.IO.Path.GetFileName(f)}' appears to be a client-side rendering mod. " +
                        "Installing this on a server will almost certainly cause a crash.\n\n" +
                        "Do you want to skip this mod?", 
                        DialogType.Question);

                    if (res == DialogResult.Yes) continue;
                }

                var dir = System.IO.Path.Combine(_serverDir, "mods");
                Directory.CreateDirectory(dir);
                await FileUtils.CopyFileAsync(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)), true);
            }
            LoadAddons(); _onAddonChanged();
        }

        private async Task DeleteModAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {System.IO.Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try 
                { 
                    await FileUtils.DeleteFileAsync(path); 
                    await _manifestService.UnregisterByFileNameAsync(_serverDir, Path.GetFileName(path));
                    LoadAddons(); 
                    _onAddonChanged(); 
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        // ── Modrinth / browser navigation ─────────────────────────────────

        private void BrowseModrinth(string projectType) => BrowseModrinthInternal(projectType, lockToPoggit: false);

        private void BrowseModrinthInternal(string projectType, bool lockToPoggit)
        {
            // For BDS, we never show the web browser — use local import instead.
            if (IsBedrockDedicated)
            {
                _dialogService.ShowMessage(
                    "Local Import Required",
                    "Bedrock add-ons cannot be browsed from a URL. Use the 'Import Local Add-on' button to install .mcpack or .mcaddon files.",
                    DialogType.Information);
                return;
            }

            try
            {
                var browserPage = (PluginBrowserPage)ActivatorUtilities.CreateInstance(
                    _serviceProvider,
                    typeof(PluginBrowserPage),
                    new object[]
                    {
                        _serverDir,
                        _metadata.MinecraftVersion,
                        projectType,
                        (Action)(() => { LoadAddons(); _onAddonChanged(); }),
                        _metadata.Compatibility
                    });

                if (projectType == "project_type:modpack")
                    browserPage.OnModpackDownloaded += async tempZip =>
                    {
                        await ImportModpackActionAsync(tempZip);
                        try { File.Delete(tempZip); } catch { }
                    };

                _navigationService.NavigateToDetailPage(
                    browserPage, "Marketplace",
                    DetailRouteKind.PluginBrowser,
                    DetailBackNavigation.PreviousDetail);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Failed", ex.Message, DialogType.Error);
            }
        }

        private async Task ImportModpackAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Modpack ZIP", "ZIP Files (*.zip)|*.zip");
            if (file != null) await ImportModpackActionAsync(file);
        }

        private async Task ImportModpackActionAsync(string zipPath)
        {
            try
            {
                var result = await _modpackService.ParseModpackZipAsync(zipPath);
                if (await _dialogService.ShowDialogAsync("Import Modpack", $"Import modpack '{result.Name}'?", DialogType.Question) == DialogResult.Yes)
                {
                    await _modpackService.ImportToExistingInstanceAsync(result, _metadata, _serverDir, zipPath);
                    LoadAddons(); _onAddonChanged();
                }
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
        }

        // ── Update addon logic ──────────────────────────────────────────────────

        private async Task UpdateAddonAsync(PluginItemViewModel? vm)
        {
            if (vm == null) return;
            await UpdateAddonCoreAsync(
                vm.ManifestEntry,
                System.IO.Path.GetFileName(vm.Path),
                vm.Name,
                s => vm.UpdateStatusText = s,
                b => vm.IsUpdating = b);
        }

        private async Task UpdateAddonAsync(ModItemViewModel? vm)
        {
            if (vm == null) return;
            await UpdateAddonCoreAsync(
                vm.ManifestEntry,
                System.IO.Path.GetFileName(vm.Path),
                vm.Name,
                s => vm.UpdateStatusText = s,
                b => vm.IsUpdating = b);
        }

        /// <summary>
        /// Core update logic shared by plugin and mod update commands.
        /// Checks for available update via provider API, prompts user, downloads and replaces.
        /// </summary>
        private async Task UpdateAddonCoreAsync(
            AddonManifestEntry? manifestEntry,
            string fileName,
            string displayName,
            Action<string> setStatus,
            Action<bool> setUpdating)
        {
            setUpdating(true);
            setStatus("Checking for updates...");

            try
            {
                if (manifestEntry == null)
                {
                    // Not tracked in manifest — manual import
                    setStatus("");
                    _dialogService.ShowMessage(
                        "Not Tracked",
                        $"'{displayName}' was not installed from a marketplace.\n\n" +
                        "Update checking is only available for addons installed via the marketplace. " +
                        "To update manually, delete this file and re-add the new version.",
                        DialogType.Information);
                    return;
                }

                var result = await _updateService.CheckForUpdateFromEntryAsync(
                    manifestEntry,
                    _metadata.MinecraftVersion,
                    _metadata.Compatibility.LoaderName,
                    _metadata.Compatibility);

                if (result.Error != null)
                {
                    setStatus("");
                    _dialogService.ShowMessage("Update Check Failed", result.Error, DialogType.Warning);
                    return;
                }

                if (result.IsUpdateAvailable)
                {
                    // Update available — confirm with user
                    setStatus($"Update available: {result.LatestVersionName}");

                    var confirm = await _dialogService.ShowDialogAsync(
                        "Update Available",
                        $"A new version of '{displayName}' is available.\n\n" +
                        $"Installed: {manifestEntry.VersionId}\n" +
                        $"Latest: {result.LatestVersionName}\n\n" +
                        "Do you want to update now?",
                        DialogType.Question);

                    if (confirm != DialogResult.Yes)
                    {
                        setStatus("");
                        return;
                    }

                    setStatus("Downloading update...");

                    await _updateService.ApplyUpdateAsync(
                        _serverDir,
                        fileName,
                        result,
                        manifestEntry.Provider,
                        manifestEntry.ProjectId,
                        _metadata.Compatibility);

                    setStatus("Updated ✔");
                    _dialogService.ShowMessage("Updated",
                        $"'{displayName}' has been updated to {result.LatestVersionName}.");

                    LoadAddons();
                    _onAddonChanged();
                }
                else
                {
                    // No update available — offer reinstall
                    setStatus("Up to date");

                    var reinstall = await _dialogService.ShowDialogAsync(
                        "Already Up To Date",
                        $"'{displayName}' is already on the latest version ({result.LatestVersionName}).\n\n" +
                        "Would you like to reinstall (re-download) the current version anyway?",
                        DialogType.Question);

                    if (reinstall != DialogResult.Yes)
                    {
                        // Clear status after a delay
                        await Task.Delay(2000);
                        setStatus("");
                        return;
                    }

                    setStatus("Reinstalling...");

                    await _updateService.ApplyUpdateAsync(
                        _serverDir,
                        fileName,
                        result,
                        manifestEntry.Provider,
                        manifestEntry.ProjectId,
                        _metadata.Compatibility);

                    setStatus("Reinstalled ✔");
                    _dialogService.ShowMessage("Reinstalled",
                        $"'{displayName}' has been reinstalled ({result.LatestVersionName}).");

                    LoadAddons();
                    _onAddonChanged();
                }
            }
            catch (Exception ex)
            {
                setStatus("Update failed");
                _dialogService.ShowMessage("Update Failed", ex.Message, DialogType.Error);
            }
            finally
            {
                setUpdating(false);
            }
        }

        // ── Update All logic ──────────────────────────────────────────────────

        /// <summary>
        /// Batch-checks all marketplace-tracked addons for updates, shows a summary
        /// confirmation, then applies all confirmed updates sequentially.
        /// </summary>
        private async Task UpdateAllAddonsAsync(bool isPlugins)
        {
            IsUpdatingAll = true;
            UpdateAllStatusText = "Scanning for updates...";

            try
            {
                // 1. Collect all marketplace-tracked items
                var trackedItems = isPlugins
                    ? Plugins.Where(p => p.ManifestEntry != null)
                             .Select(p => (Name: p.Name, FileName: System.IO.Path.GetFileName(p.Path), Entry: p.ManifestEntry!, VM: (object)p))
                             .ToList()
                    : Mods.Where(m => m.ManifestEntry != null)
                          .Select(m => (Name: m.Name, FileName: System.IO.Path.GetFileName(m.Path), Entry: m.ManifestEntry!, VM: (object)m))
                          .ToList();

                if (trackedItems.Count == 0)
                {
                    UpdateAllStatusText = "";
                    _dialogService.ShowMessage("No Tracked Addons",
                        "No addons were installed from a marketplace. Update checking is only available for marketplace-installed items.",
                        DialogType.Information);
                    return;
                }

                // 2. Check each addon for updates (parallel-safe: each call is independent)
                var updateResults = new List<(string Name, string FileName, AddonManifestEntry Entry, AddonUpdateCheckResult Result, object VM)>();
                int checked_count = 0;

                foreach (var item in trackedItems)
                {
                    checked_count++;
                    UpdateAllStatusText = $"Checking {checked_count}/{trackedItems.Count}: {item.Name}...";

                    // Set per-item status
                    SetItemStatus(item.VM, "Checking...", true);

                    var result = await _updateService.CheckForUpdateFromEntryAsync(
                        item.Entry,
                        _metadata.MinecraftVersion,
                        _metadata.Compatibility.LoaderName,
                        _metadata.Compatibility);

                    if (result.Error != null)
                    {
                        SetItemStatus(item.VM, "Check failed", false);
                    }
                    else if (result.IsUpdateAvailable)
                    {
                        SetItemStatus(item.VM, $"Update: {result.LatestVersionName}", false);
                        updateResults.Add((item.Name, item.FileName, item.Entry, result, item.VM));
                    }
                    else
                    {
                        SetItemStatus(item.VM, "Up to date ✔", false);
                    }
                }

                // 3. Summary & Confirmation
                if (updateResults.Count == 0)
                {
                    UpdateAllStatusText = "All addons are up to date!";
                    _dialogService.ShowMessage("All Up To Date",
                        $"All {trackedItems.Count} marketplace addon(s) are already on their latest versions.");

                    // Clear status after delay
                    await Task.Delay(3000);
                    UpdateAllStatusText = "";
                    ClearAllItemStatus(isPlugins);
                    return;
                }

                UpdateAllStatusText = $"{updateResults.Count} update(s) available";

                var nameList = string.Join("\n", updateResults.Select(u =>
                    $"  • {u.Name}  →  {u.Result.LatestVersionName}"));

                var confirm = await _dialogService.ShowDialogAsync(
                    "Updates Available",
                    $"{updateResults.Count} of {trackedItems.Count} addon(s) have updates:\n\n{nameList}\n\nDo you want to install all updates now?",
                    DialogType.Question);

                if (confirm != DialogResult.Yes)
                {
                    UpdateAllStatusText = "";
                    ClearAllItemStatus(isPlugins);
                    return;
                }

                // 4. Apply updates sequentially
                int applied = 0;
                int failed = 0;

                foreach (var update in updateResults)
                {
                    applied++;
                    UpdateAllStatusText = $"Updating {applied}/{updateResults.Count}: {update.Name}...";
                    SetItemStatus(update.VM, "Downloading...", true);

                    try
                    {
                        await _updateService.ApplyUpdateAsync(
                            _serverDir,
                            update.FileName,
                            update.Result,
                            update.Entry.Provider,
                            update.Entry.ProjectId,
                            _metadata.Compatibility);

                        SetItemStatus(update.VM, "Updated ✔", false);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        SetItemStatus(update.VM, $"Failed: {ex.Message}", false);
                    }
                }

                // 5. Reload and report
                LoadAddons();
                _onAddonChanged();

                int succeeded = applied - failed;
                UpdateAllStatusText = failed == 0
                    ? $"All {succeeded} update(s) installed ✔"
                    : $"{succeeded} updated, {failed} failed";

                _dialogService.ShowMessage("Update Complete",
                    failed == 0
                        ? $"Successfully updated {succeeded} addon(s)."
                        : $"{succeeded} addon(s) updated, {failed} failed. Check individual statuses for details.",
                    failed == 0 ? DialogType.Information : DialogType.Warning);

                await Task.Delay(5000);
                UpdateAllStatusText = "";
            }
            catch (Exception ex)
            {
                UpdateAllStatusText = "Update All failed";
                _dialogService.ShowMessage("Update All Failed", ex.Message, DialogType.Error);
            }
            finally
            {
                IsUpdatingAll = false;
            }
        }

        private static void SetItemStatus(object vm, string status, bool isUpdating)
        {
            if (vm is PluginItemViewModel pvm)
            {
                pvm.UpdateStatusText = status;
                pvm.IsUpdating = isUpdating;
            }
            else if (vm is ModItemViewModel mvm)
            {
                mvm.UpdateStatusText = status;
                mvm.IsUpdating = isUpdating;
            }
        }

        private void ClearAllItemStatus(bool isPlugins)
        {
            if (isPlugins)
            {
                foreach (var p in Plugins) { p.UpdateStatusText = ""; p.IsUpdating = false; }
            }
            else
            {
                foreach (var m in Mods) { m.UpdateStatusText = ""; m.IsUpdating = false; }
            }
        }
    }

    // ── View models ───────────────────────────────────────────────────────

    public class PluginItemViewModel : Core.Mvvm.ViewModelBase
    {
        private bool _isUpdating;
        private string _updateStatusText = "";

        public string Name        { get; set; } = "";
        public string Path        { get; set; } = "";
        public string ApiVersion  { get; set; } = "";
        public double SizeKb      { get; set; }
        public bool   IsMismatch  { get; set; }
        public DateTime LastModified { get; set; }

        /// <summary>Reference to the manifest entry for provider/project lookups.</summary>
        public AddonManifestEntry? ManifestEntry { get; set; }

        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
        }

        public string UpdateStatusText
        {
            get => _updateStatusText;
            set => SetProperty(ref _updateStatusText, value);
        }

        /// <summary>True when this addon is tracked in the manifest (marketplace-installed).</summary>
        public bool IsTracked => ManifestEntry != null;
    }

    public class ModItemViewModel : Core.Mvvm.ViewModelBase
    {
        private bool _isUpdating;
        private string _updateStatusText = "";

        public string Name        { get; set; } = "";
        public string Path        { get; set; } = "";
        public double SizeKb      { get; set; }
        public DateTime LastModified { get; set; }
        /// <summary>"behavior" | "resource" for BDS; empty for Java mods.</summary>
        public string AddonType   { get; set; } = "";

        /// <summary>Reference to the manifest entry for provider/project lookups.</summary>
        public AddonManifestEntry? ManifestEntry { get; set; }

        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
        }

        public string UpdateStatusText
        {
            get => _updateStatusText;
            set => SetProperty(ref _updateStatusText, value);
        }

        /// <summary>True when this addon is tracked in the manifest (marketplace-installed).</summary>
        public bool IsTracked => ManifestEntry != null;
    }

}
