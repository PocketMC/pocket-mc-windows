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
using System.Windows.Media;

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
        private readonly AddonInventoryService _inventoryService;
        private readonly AddonToggleService _toggleService;
        private readonly AddonUpdateCheckService _updateCheckService;
        private readonly AddonUpdateService _updateService;

        // ── Installed addon collections ──────────────────────────────────
        private List<PluginItemViewModel> _allPlugins = new();
        private List<ModItemViewModel> _allMods = new();

        public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
        public ObservableCollection<ModItemViewModel> Mods { get; } = new();

        // ── Search & Filter ──────────────────────────────────────────────
        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFiltersAndSort();
                }
            }
        }

        private string _selectedSortOption = "Name";
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (SetProperty(ref _selectedSortOption, value))
                {
                    ApplyFiltersAndSort();
                }
            }
        }

        public List<string> SortOptions { get; } = new()
        {
            "Name", "Last Modified", "Size", "Loader Type", "Source", "Warnings First"
        };

        public int WarningsCount => _allMods.Count(m => m.HasWarnings) + _allPlugins.Count(p => p.HasWarnings);
        public bool HasWarningsBanner => WarningsCount > 0;
        public bool IsServerRunning => _isRunningCheck();
        public bool ShowServerRunningAddonMessage => IsServerRunning && (_allMods.Count > 0 || _allPlugins.Count > 0);
        public string ServerRunningAddonMessage => "Stop the server before enabling or disabling mods/plugins.";

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
        public ICommand InstallAddonUpdateCommand { get; }

        // Extra context commands
        public ICommand OpenFolderCommand { get; }
        public ICommand ToggleModActiveCommand { get; }

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
            _inventoryService  = serviceProvider.GetRequiredService<AddonInventoryService>();
            _toggleService     = serviceProvider.GetRequiredService<AddonToggleService>();
            _updateCheckService = serviceProvider.GetRequiredService<AddonUpdateCheckService>();
            _updateService      = serviceProvider.GetRequiredService<AddonUpdateService>();

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
                p => !_isUpdatingAll && p is PluginItemViewModel { IsUpdating: false });
            UpdateModCommand = new RelayCommand(
                async p => await UpdateAddonAsync(p as ModItemViewModel),
                p => !_isUpdatingAll && p is ModItemViewModel { IsUpdating: false, IsDisabled: false });
            UpdateAllPluginsCommand = new RelayCommand(
                async _ => await UpdateAllAddonsAsync(isPlugins: true),
                _ => !_isUpdatingAll && Plugins.Any(p => p.IsTracked));
            UpdateAllModsCommand = new RelayCommand(
                async _ => await UpdateAllAddonsAsync(isPlugins: false),
                _ => !_isUpdatingAll && Mods.Any(m => m.IsTracked));
            InstallAddonUpdateCommand = new RelayCommand(
                async p => await InstallUpdateForAddonAsync(p),
                p => p is ModItemViewModel { UpdateStatus: AddonUpdateStatus.UpdateAvailable, IsUpdating: false } or
                     PluginItemViewModel { UpdateStatus: AddonUpdateStatus.UpdateAvailable, IsUpdating: false });

            OpenFolderCommand = new RelayCommand(p => OpenContainingFolder(p as string));
            ToggleModActiveCommand = new RelayCommand(async p => await ToggleAddonStateAsync(p), CanToggleAddon);
        }

        public void LoadAddons()
        {
            LoadAddonsInternal(false);
        }

        internal void LoadAddonsSync()
        {
            LoadAddonsInternal(true);
        }

        private void LoadAddonsInternal(bool runSync)
        {
            Action action = () =>
            {
                var manifest = _manifestService.LoadManifest(_serverDir);

                if (IsBedrockDedicated)
                {
                    var items = BuildBedrockAddonList();
                    _allMods = items;
                    _allPlugins = new List<PluginItemViewModel>();
                    ApplyFiltersAndSort();
                }
                else if (IsPocketmine)
                {
                    var items = BuildPocketminePluginList(manifest);
                    _allPlugins = items;
                    _allMods = new List<ModItemViewModel>();
                    ApplyFiltersAndSort();
                }
                else
                {
                    var inventory = _inventoryService.ScanAsync(_metadata, _serverDir).GetAwaiter().GetResult();
                    var pluginItems = inventory
                        .Where(item => item.Kind == AddonKind.Plugin)
                        .Select(CreatePluginViewModel)
                        .ToList();
                    var modItems = inventory
                        .Where(item => item.Kind == AddonKind.Mod)
                        .Select(CreateModViewModel)
                        .ToList();
                    _allPlugins = pluginItems;
                    _allMods = modItems;
                    ApplyFiltersAndSort();
                }
            };

            if (runSync)
            {
                action();
            }
            else
            {
                _ = Task.Run(action);
            }
        }

        private static void DispatchToUI(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        public void RefreshRunningState()
        {
            foreach (var plugin in _allPlugins)
            {
                plugin.CanEnable = plugin.IsDisabled && !IsServerRunning;
                plugin.CanDisable = !plugin.IsDisabled && !IsServerRunning;
            }

            foreach (var mod in _allMods)
            {
                mod.CanEnable = mod.IsDisabled && !IsServerRunning;
                mod.CanDisable = !mod.IsDisabled && !IsServerRunning;
            }

            OnPropertyChanged(nameof(IsServerRunning));
            OnPropertyChanged(nameof(ShowServerRunningAddonMessage));
            ApplyFiltersAndSort();
            CommandManager.InvalidateRequerySuggested();
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
                    DisplayName  = addon.Name,
                    FileName     = Path.GetFileName(addon.FilePath),
                    Path         = addon.FilePath,
                    SizeKb       = addon.SizeKb,
                    LastModified = addon.LastModified,
                    AddonType    = addon.AddonType,
                    SourceLabel  = "Manual",
                    Icon         = AddonIconService.BedrockFallback
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

                string sourceLabel = entry != null ? (entry.Provider ?? "Manual") : "Manual";

                result.Add(new PluginItemViewModel
                {
                    Name         = entry?.DisplayName ?? entry?.ProjectTitle ?? fi.Name,
                    FileName     = fi.Name,
                    Path         = file,
                    ApiVersion   = "PocketMine",
                    SizeKb       = fi.Length / 1024.0,
                    IsMismatch   = false,
                    LastModified = fi.LastWriteTime,
                    ManifestEntry = entry,
                    SourceLabel  = sourceLabel,
                    Icon         = AddonIconService.PluginFallback
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

        private PluginItemViewModel CreatePluginViewModel(AddonInventoryItem item)
        {
            AddonManifestEntry? entry = FindManifestEntry(item);
            return new PluginItemViewModel
            {
                Name = item.DisplayName,
                DisplayName = item.DisplayName,
                Path = item.FullPath,
                RelativePath = item.RelativePath,
                ApiVersion = item.Version ?? item.LoaderType,
                SizeKb = item.SizeBytes / 1024.0,
                IsMismatch = item.UpdateStatus == AddonUpdateStatus.PossiblyIncompatible,
                LastModified = item.LastModifiedUtc == DateTime.MinValue ? DateTime.MinValue : item.LastModifiedUtc.ToLocalTime(),
                ManifestEntry = entry,
                FileName = item.FileName,
                Version = item.Version,
                LoaderType = item.LoaderType,
                SideLabel = item.SideLabel,
                SourceLabel = item.Provenance?.Provider ?? "Manual",
                Icon = AddonIconService.GetIcon(item.FullPath, "Plugin", item.IconBytes),
                HasWarnings = item.Warnings.Count > 0,
                WarningText = string.Join(Environment.NewLine, item.Warnings),
                IsDisabled = item.State == AddonState.Disabled,
                State = item.State,
                Kind = item.Kind,
                UpdateStatus = item.UpdateStatus,
                UpdateInfo = item.UpdateInfo,
                CanEnable = item.CanEnable,
                CanDisable = item.CanDisable,
                RequiresServerStopped = item.RequiresServerStopped
            };
        }

        private ModItemViewModel CreateModViewModel(AddonInventoryItem item)
        {
            AddonManifestEntry? entry = FindManifestEntry(item);
            return new ModItemViewModel
            {
                Name = item.DisplayName,
                DisplayName = item.DisplayName,
                FileName = item.FileName,
                Path = item.FullPath,
                RelativePath = item.RelativePath,
                SizeKb = item.SizeBytes / 1024.0,
                LastModified = item.LastModifiedUtc == DateTime.MinValue ? DateTime.MinValue : item.LastModifiedUtc.ToLocalTime(),
                ManifestEntry = entry,
                Version = item.Version,
                LoaderType = item.LoaderType,
                SourceLabel = item.Provenance?.Provider ?? "Manual",
                Icon = AddonIconService.GetIcon(item.FullPath, item.LoaderType, item.IconBytes),
                HasWarnings = item.Warnings.Count > 0,
                WarningText = string.Join(Environment.NewLine, item.Warnings),
                SideSupport = item.SideSupport,
                SideLabel = item.SideLabel,
                IsClientOnly = item.SideSupport == ModSideSupport.ClientOnly,
                IsMetadataUnknown = item.LoaderType == "Unknown",
                IsDisabled = item.State == AddonState.Disabled,
                State = item.State,
                Kind = item.Kind,
                UpdateStatus = item.UpdateStatus,
                UpdateInfo = item.UpdateInfo,
                CanEnable = item.CanEnable,
                CanDisable = item.CanDisable,
                RequiresServerStopped = item.RequiresServerStopped
            };
        }

        private AddonManifestEntry? FindManifestEntry(AddonInventoryItem item)
        {
            var manifest = _manifestService.LoadManifest(_serverDir);
            return manifest.Entries.FirstOrDefault(entry =>
                entry.FileName.Equals(item.FileName, StringComparison.OrdinalIgnoreCase) ||
                entry.FileName.Equals(Path.GetFileName(item.RelativePath), StringComparison.OrdinalIgnoreCase));
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
                vm.Kind,
                vm.State,
                vm.RelativePath,
                vm.Path,
                vm.LoaderTypeForUpdate,
                vm.Version,
                vm.Name,
                s => vm.UpdateStatusText = s,
                b => vm.IsUpdating = b,
                status => vm.UpdateStatus = status,
                info => vm.UpdateInfo = info,
                vm.ManifestEntry);
        }

        private async Task UpdateAddonAsync(ModItemViewModel? vm)
        {
            if (vm == null) return;
            await UpdateAddonCoreAsync(
                vm.Kind,
                vm.State,
                vm.RelativePath,
                vm.Path,
                vm.LoaderType,
                vm.Version,
                vm.Name,
                s => vm.UpdateStatusText = s,
                b => vm.IsUpdating = b,
                status => vm.UpdateStatus = status,
                info => vm.UpdateInfo = info,
                vm.ManifestEntry);
        }

        /// <summary>
        /// Core update logic shared by plugin and mod update commands.
        /// Checks for available update via provider API, prompts user, downloads and replaces.
        /// </summary>
        private async Task UpdateAddonCoreAsync(
            AddonKind kind,
            AddonState state,
            string relativePath,
            string fullPath,
            string loaderType,
            string? version,
            string displayName,
            Action<string> setStatus,
            Action<bool> setUpdating,
            Action<AddonUpdateStatus> setUpdateStatus,
            Action<AddonUpdateInfo?> setUpdateInfo,
            AddonManifestEntry? manifestEntry = null)
        {
            setUpdating(true);
            setStatus("Checking for updates...");
            setUpdateStatus(AddonUpdateStatus.Checking);

            try
            {
                var inventoryItem = new AddonInventoryItem
                {
                    InstanceId = _metadata.Id,
                    Kind = kind,
                    State = state,
                    DisplayName = displayName,
                    FileName = Path.GetFileName(relativePath),
                    RelativePath = relativePath,
                    FullPath = fullPath,
                    LoaderType = loaderType,
                    Version = version,
                    SideSupport = ModSideSupport.Unknown,
                    SideLabel = "Side unknown",
                    Dependencies = Array.Empty<string>(),
                    Warnings = Array.Empty<string>()
                };

                AddonUpdateCheckResultModel result = await _updateCheckService.CheckAsync(_metadata, _serverDir, inventoryItem);
                setUpdateStatus(result.Status);
                setUpdateInfo(result.UpdateInfo);
                setStatus(FormatPassiveUpdateStatus(result, displayName));

                // Prompt user to install when an update is found
                if (result.Status == AddonUpdateStatus.UpdateAvailable && result.UpdateInfo != null && manifestEntry != null)
                {
                    string latestVersion = result.UpdateInfo.LatestVersionName ?? result.UpdateInfo.LatestVersionId ?? "new version";
                    string installedVersion = version ?? "unknown";
                    string message = BuildUpdateConfirmationMessage(
                        displayName, installedVersion, latestVersion,
                        result.UpdateInfo.Warnings?.ToList() ?? new List<string>());

                    var dialogResult = await _dialogService.ShowDialogAsync("Update Available", message, DialogType.Question);
                    if (dialogResult == DialogResult.Yes)
                    {
                        setStatus("Installing update...");
                        setUpdating(true);
                        await PerformUpdateInstallAsync(
                            inventoryItem.FileName,
                            result.UpdateInfo,
                            manifestEntry.Provider,
                            manifestEntry.ProjectId,
                            displayName,
                            setStatus,
                            setUpdateStatus);
                    }
                }
            }
            catch (Exception ex)
            {
                setUpdateStatus(AddonUpdateStatus.ProviderError);
                setStatus("Update check failed");
                _dialogService.ShowMessage("Update Check Failed", ex.Message, DialogType.Error);
            }
            finally
            {
                setUpdating(false);
            }
        }

        /// <summary>
        /// Downloads and installs an addon update using AddonUpdateService.ApplyUpdateAsync,
        /// then refreshes the addon list.
        /// </summary>
        private async Task PerformUpdateInstallAsync(
            string oldFileName,
            AddonUpdateInfo updateInfo,
            string provider,
            string projectId,
            string displayName,
            Action<string> setStatus,
            Action<AddonUpdateStatus> setUpdateStatus)
        {
            try
            {
                var checkResult = new AddonUpdateCheckResult
                {
                    IsUpdateAvailable = true,
                    LatestVersionId = updateInfo.LatestVersionId,
                    LatestVersionName = updateInfo.LatestVersionName,
                    LatestFileName = updateInfo.LatestFileName,
                    LatestDownloadUrl = updateInfo.LatestDownloadUrl,
                    ProjectTitle = updateInfo.ProjectTitle,
                    Hash = updateInfo.Hash,
                    HashType = updateInfo.HashType,
                    ReleaseType = updateInfo.ReleaseType,
                    Warnings = updateInfo.Warnings?.ToList() ?? new List<string>()
                };

                await _updateService.ApplyUpdateAsync(
                    _serverDir,
                    oldFileName,
                    checkResult,
                    provider,
                    projectId,
                    _metadata.Compatibility);

                setUpdateStatus(AddonUpdateStatus.UpToDate);
                setStatus($"Updated to {updateInfo.LatestVersionName ?? updateInfo.LatestVersionId ?? "latest"}");
                _dialogService.ShowMessage("Update Installed",
                    $"'{displayName}' has been updated to {updateInfo.LatestVersionName ?? updateInfo.LatestVersionId ?? "the latest version"}.",
                    DialogType.Information);
                LoadAddons();
                _onAddonChanged();
            }
            catch (Exception ex)
            {
                setUpdateStatus(AddonUpdateStatus.ProviderError);
                setStatus("Update install failed");
                _dialogService.ShowMessage("Update Failed",
                    $"Could not install update for '{displayName}': {ex.Message}",
                    DialogType.Error);
            }
        }

        /// <summary>
        /// Handles install-update requests from the UI button (works for both mod and plugin VMs).
        /// </summary>
        private async Task InstallUpdateForAddonAsync(object? param)
        {
            switch (param)
            {
                case ModItemViewModel mod when mod.UpdateInfo != null && mod.ManifestEntry != null:
                    mod.IsUpdating = true;
                    mod.UpdateStatusText = "Installing update...";
                    try
                    {
                        await PerformUpdateInstallAsync(
                            mod.FileName,
                            mod.UpdateInfo,
                            mod.ManifestEntry.Provider,
                            mod.ManifestEntry.ProjectId,
                            mod.Name,
                            s => mod.UpdateStatusText = s,
                            status => mod.UpdateStatus = status);
                    }
                    finally
                    {
                        mod.IsUpdating = false;
                    }
                    break;

                case PluginItemViewModel plugin when plugin.UpdateInfo != null && plugin.ManifestEntry != null:
                    plugin.IsUpdating = true;
                    plugin.UpdateStatusText = "Installing update...";
                    try
                    {
                        await PerformUpdateInstallAsync(
                            plugin.FileName,
                            plugin.UpdateInfo,
                            plugin.ManifestEntry.Provider,
                            plugin.ManifestEntry.ProjectId,
                            plugin.Name,
                            s => plugin.UpdateStatusText = s,
                            status => plugin.UpdateStatus = status);
                    }
                    finally
                    {
                        plugin.IsUpdating = false;
                    }
                    break;
            }
        }

        // ── Update All logic ──────────────────────────────────────────────────

        private static string FormatPassiveUpdateStatus(AddonUpdateCheckResultModel result, string displayName)
        {
            return result.Status switch
            {
                AddonUpdateStatus.UnknownSource => "Manual source - update check unavailable",
                AddonUpdateStatus.UnsupportedProvider => result.Message ?? "Unsupported provider",
                AddonUpdateStatus.ProviderError => result.Message ?? "Provider error",
                AddonUpdateStatus.PossiblyIncompatible => result.Message ?? "Possibly incompatible",
                AddonUpdateStatus.UpdateAvailable => result.Message ?? $"Update available for {displayName}",
                AddonUpdateStatus.UpToDate => "Up to date",
                AddonUpdateStatus.Checking => "Checking...",
                _ => result.Message ?? "Update status unknown"
            };
        }

        private async Task<AddonUpdateCheckResultModel> CheckPassiveUpdateAsync(PluginItemViewModel plugin)
        {
            var item = new AddonInventoryItem
            {
                InstanceId = _metadata.Id,
                Kind = plugin.Kind,
                State = plugin.State,
                DisplayName = plugin.Name,
                FileName = plugin.FileName,
                RelativePath = plugin.RelativePath,
                FullPath = plugin.Path,
                LoaderType = plugin.LoaderTypeForUpdate,
                Version = plugin.Version,
                SideSupport = ModSideSupport.ServerOnly,
                SideLabel = "Server-only",
                Dependencies = Array.Empty<string>(),
                Warnings = Array.Empty<string>()
            };

            AddonUpdateCheckResultModel result = await _updateCheckService.CheckAsync(_metadata, _serverDir, item);
            plugin.UpdateStatus = result.Status;
            plugin.UpdateInfo = result.UpdateInfo;
            return result;
        }

        private async Task<AddonUpdateCheckResultModel> CheckPassiveUpdateAsync(ModItemViewModel mod)
        {
            var item = new AddonInventoryItem
            {
                InstanceId = _metadata.Id,
                Kind = mod.Kind,
                State = mod.State,
                DisplayName = mod.Name,
                FileName = mod.FileName,
                RelativePath = mod.RelativePath,
                FullPath = mod.Path,
                LoaderType = mod.LoaderType,
                Version = mod.Version,
                SideSupport = mod.SideSupport,
                SideLabel = mod.SideLabel,
                Dependencies = Array.Empty<string>(),
                Warnings = Array.Empty<string>()
            };

            AddonUpdateCheckResultModel result = await _updateCheckService.CheckAsync(_metadata, _serverDir, item);
            mod.UpdateStatus = result.Status;
            mod.UpdateInfo = result.UpdateInfo;
            return result;
        }

        /// <summary>
        /// Batch-checks marketplace-tracked add-ons and reports passive status only.
        /// </summary>
        private async Task UpdateAllAddonsAsync(bool isPlugins)
        {
            IsUpdatingAll = true;
            UpdateAllStatusText = "Scanning for updates...";

            try
            {
                var trackedItems = isPlugins
                    ? Plugins.Where(p => p.ManifestEntry != null && !p.IsDisabled)
                             .Select(p => (Name: p.Name, VM: (object)p))
                             .ToList()
                    : Mods.Where(m => m.ManifestEntry != null && !m.IsDisabled)
                          .Select(m => (Name: m.Name, VM: (object)m))
                          .ToList();

                if (trackedItems.Count == 0)
                {
                    UpdateAllStatusText = "";
                    _dialogService.ShowMessage("No Tracked Addons",
                        "No addons were installed from a marketplace. Update checking is only available for marketplace-installed items.",
                        DialogType.Information);
                    return;
                }

                int available = 0;
                int failed = 0;
                int checked_count = 0;

                foreach (var item in trackedItems)
                {
                    checked_count++;
                    UpdateAllStatusText = $"Checking {checked_count}/{trackedItems.Count}: {item.Name}...";
                    SetItemStatus(item.VM, "Checking...", true);

                    AddonUpdateCheckResultModel result = item.VM switch
                    {
                        PluginItemViewModel plugin => await CheckPassiveUpdateAsync(plugin),
                        ModItemViewModel mod => await CheckPassiveUpdateAsync(mod),
                        _ => new AddonUpdateCheckResultModel { Status = AddonUpdateStatus.Unknown }
                    };

                    if (result.Status == AddonUpdateStatus.UpdateAvailable)
                    {
                        available++;
                    }
                    else if (result.Status == AddonUpdateStatus.ProviderError ||
                             result.Status == AddonUpdateStatus.UnsupportedProvider)
                    {
                        failed++;
                    }

                    SetItemStatus(item.VM, FormatPassiveUpdateStatus(result, item.Name), false);
                }

                UpdateAllStatusText = failed > 0
                    ? $"{available} update(s) available, {failed} check(s) failed"
                    : $"{available} update(s) available";

                // Prompt user to batch-install all available updates
                if (available > 0)
                {
                    var updatableItems = trackedItems
                        .Where(t => t.VM switch
                        {
                            PluginItemViewModel p => p.UpdateStatus == AddonUpdateStatus.UpdateAvailable && p.UpdateInfo != null && p.ManifestEntry != null,
                            ModItemViewModel m => m.UpdateStatus == AddonUpdateStatus.UpdateAvailable && m.UpdateInfo != null && m.ManifestEntry != null,
                            _ => false
                        })
                        .ToList();

                    if (updatableItems.Count > 0)
                    {
                        var updateEntries = updatableItems.Select(t => t.VM switch
                        {
                            PluginItemViewModel p => (p.Name, p.UpdateInfo!.LatestVersionName ?? p.UpdateInfo.LatestVersionId ?? "new version"),
                            ModItemViewModel m => (m.Name, m.UpdateInfo!.LatestVersionName ?? m.UpdateInfo.LatestVersionId ?? "new version"),
                            _ => (t.Name, "new version")
                        }).ToList();

                        var allWarnings = updatableItems.SelectMany(t => t.VM switch
                        {
                            PluginItemViewModel p => p.UpdateInfo?.Warnings ?? Array.Empty<string>(),
                            ModItemViewModel m => m.UpdateInfo?.Warnings ?? Array.Empty<string>(),
                            _ => Array.Empty<string>()
                        }).ToList();

                        string message = BuildBatchUpdateSummaryMessage(
                            updatableItems.Count, trackedItems.Count, updateEntries, allWarnings);

                        var dialogResult = await _dialogService.ShowDialogAsync(
                            "Install All Updates", message, DialogType.Question);

                        if (dialogResult == DialogResult.Yes)
                        {
                            int installed = 0;
                            int installFailed = 0;

                            foreach (var updatable in updatableItems)
                            {
                                installed++;
                                UpdateAllStatusText = $"Installing {installed}/{updatableItems.Count}: {updatable.Name}...";
                                SetItemStatus(updatable.VM, "Installing update...", true);

                                try
                                {
                                    switch (updatable.VM)
                                    {
                                        case ModItemViewModel mod:
                                            await PerformUpdateInstallAsync(
                                                mod.FileName,
                                                mod.UpdateInfo!,
                                                mod.ManifestEntry!.Provider,
                                                mod.ManifestEntry.ProjectId,
                                                mod.Name,
                                                s => mod.UpdateStatusText = s,
                                                status => mod.UpdateStatus = status);
                                            break;

                                        case PluginItemViewModel plugin:
                                            await PerformUpdateInstallAsync(
                                                plugin.FileName,
                                                plugin.UpdateInfo!,
                                                plugin.ManifestEntry!.Provider,
                                                plugin.ManifestEntry.ProjectId,
                                                plugin.Name,
                                                s => plugin.UpdateStatusText = s,
                                                status => plugin.UpdateStatus = status);
                                            break;
                                    }
                                }
                                catch
                                {
                                    installFailed++;
                                }
                                finally
                                {
                                    SetItemStatus(updatable.VM, "", false);
                                }
                            }

                            UpdateAllStatusText = installFailed > 0
                                ? $"{installed - installFailed} updated, {installFailed} failed"
                                : $"All {installed} addon(s) updated successfully";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateAllStatusText = "Update checks failed";
                _dialogService.ShowMessage("Update Checks Failed", ex.Message, DialogType.Error);
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

        public static string FormatAddonUpdateWarningText(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0) return "";
            return "\n\nWarnings:\n" + string.Join("\n", warnings.Select(w => "• " + w));
        }

        public static string BuildUpdateConfirmationMessage(string displayName, string installedVersion, string latestVersion, List<string> warnings)
        {
            string warningText = FormatAddonUpdateWarningText(warnings);
            return $"A new version of '{displayName}' is available.\n\n" +
                   $"Installed: {installedVersion}\n" +
                   $"Latest: {latestVersion}\n\n" +
                   "Do you want to update now?" + warningText;
        }

        public static string BuildReinstallConfirmationMessage(string displayName, string latestVersion, List<string> warnings)
        {
            string warningText = FormatAddonUpdateWarningText(warnings);
            return $"'{displayName}' is already on the latest version ({latestVersion}).\n\n" +
                   "Would you like to reinstall (re-download) the current version anyway?" + warningText;
        }

        public static string BuildBatchUpdateSummaryMessage(int updateCount, int totalTrackedCount, List<(string Name, string LatestVersionName)> updates, List<string> allWarnings)
        {
            var nameList = string.Join("\n", updates.Select(u =>
                $"  • {u.Name}  →  {u.LatestVersionName}"));

            string warningText = FormatAddonUpdateWarningText(allWarnings);

            return $"{updateCount} of {totalTrackedCount} addon(s) have updates:\n\n{nameList}\n\nDo you want to install all updates now?" + warningText;
        }

        public void ApplyFiltersAndSort()
        {
            DispatchToUI(() =>
            {
                // Apply filter to plugins
                var filteredPlugins = _allPlugins.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string query = SearchText.Trim();
                    filteredPlugins = filteredPlugins.Where(p =>
                        p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.SourceLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (p.Version != null && p.Version.Contains(query, StringComparison.OrdinalIgnoreCase))
                    );
                }

                // Apply sort to plugins
                filteredPlugins = SelectedSortOption switch
                {
                    "Last Modified" => filteredPlugins.OrderByDescending(p => p.LastModified),
                    "Size" => filteredPlugins.OrderByDescending(p => p.SizeKb),
                    "Source" => filteredPlugins.OrderBy(p => p.SourceLabel).ThenBy(p => p.Name),
                    "Warnings First" => filteredPlugins.OrderByDescending(p => p.HasWarnings).ThenBy(p => p.Name),
                    _ => filteredPlugins.OrderBy(p => p.Name)
                };

                // Apply filter to mods
                var filteredMods = _allMods.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string query = SearchText.Trim();
                    filteredMods = filteredMods.Where(m =>
                        m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.LoaderType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.SourceLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (m.Version != null && m.Version.Contains(query, StringComparison.OrdinalIgnoreCase))
                    );
                }

                // Apply sort to mods
                filteredMods = SelectedSortOption switch
                {
                    "Last Modified" => filteredMods.OrderByDescending(m => m.LastModified),
                    "Size" => filteredMods.OrderByDescending(m => m.SizeKb),
                    "Loader Type" => filteredMods.OrderBy(m => m.LoaderType).ThenBy(m => m.Name),
                    "Source" => filteredMods.OrderBy(m => m.SourceLabel).ThenBy(m => m.Name),
                    "Warnings First" => filteredMods.OrderByDescending(m => m.HasWarnings).ThenBy(m => m.Name),
                    _ => filteredMods.OrderBy(m => m.Name)
                };

                // Repopulate observable collections
                Plugins.Clear();
                foreach (var p in filteredPlugins) Plugins.Add(p);

                Mods.Clear();
                foreach (var m in filteredMods) Mods.Add(m);

                OnPropertyChanged(nameof(WarningsCount));
                OnPropertyChanged(nameof(HasWarningsBanner));
                OnPropertyChanged(nameof(IsServerRunning));
                OnPropertyChanged(nameof(ShowServerRunningAddonMessage));
            });
        }

        private void OpenContainingFolder(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                string dir = Path.GetDirectoryName(path) ?? "";
                if (Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { /* Ignore */ }
        }

        private bool CanToggleAddon(object? parameter)
        {
            return parameter switch
            {
                ModItemViewModel mod => (mod.CanEnable || mod.CanDisable) && !_isRunningCheck(),
                PluginItemViewModel plugin => (plugin.CanEnable || plugin.CanDisable) && !_isRunningCheck(),
                string path => !_isRunningCheck() && !string.IsNullOrWhiteSpace(path),
                _ => false
            };
        }

        private async Task ToggleAddonStateAsync(object? parameter)
        {
            switch (parameter)
            {
                case ModItemViewModel mod:
                    await ToggleInventoryItemAsync(mod.Kind, mod.State, mod.RelativePath);
                    break;
                case PluginItemViewModel plugin:
                    await ToggleInventoryItemAsync(plugin.Kind, plugin.State, plugin.RelativePath);
                    break;
                case string path:
                    await ToggleModActiveAsync(path);
                    break;
            }
        }

        internal async Task ToggleModActiveAsync(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            string fullPath = Path.GetFullPath(path);
            ModItemViewModel? mod = _allMods.FirstOrDefault(m => string.Equals(Path.GetFullPath(m.Path), fullPath, StringComparison.OrdinalIgnoreCase));
            if (mod != null)
            {
                await ToggleInventoryItemAsync(mod.Kind, mod.State, mod.RelativePath);
            }
        }

        private async Task ToggleInventoryItemAsync(AddonKind kind, AddonState state, string relativePath)
        {
            if (_isRunningCheck())
            {
                _dialogService.ShowMessage("Server is Running", ServerRunningAddonMessage, DialogType.Warning);
                return;
            }

            AddonToggleResult result = state == AddonState.Enabled
                ? await _toggleService.DisableAsync(_metadata, _serverDir, kind, relativePath, AddonDisabledBySource.User, "User disabled")
                : await _toggleService.EnableAsync(_metadata, _serverDir, kind, relativePath);

            if (!result.Success)
            {
                string title = result.ErrorCode == AddonToggleErrorCodes.ServerRunning
                    ? "Server is Running"
                    : "Could Not Toggle Add-on";
                _dialogService.ShowMessage(title, result.Message ?? "The add-on could not be toggled.", DialogType.Warning);
                return;
            }

            LoadAddons();
            _onAddonChanged();
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

        // Extended fields for richer UI
        public string FileName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string? Version { get; set; }
        public string LoaderType { get; set; } = "Plugin";
        public string SideLabel { get; set; } = "Server-only";
        public string SourceLabel { get; set; } = "Manual";
        public ImageSource? Icon { get; set; }
        public bool HasWarnings { get; set; }
        public string WarningText { get; set; } = "";
        public bool IsDisabled { get; set; }
        public AddonKind Kind { get; set; } = AddonKind.Plugin;
        public AddonState State { get; set; } = AddonState.Enabled;
        private AddonUpdateStatus _updateStatus = AddonUpdateStatus.Unknown;
        public AddonUpdateStatus UpdateStatus
        {
            get => _updateStatus;
            set => SetProperty(ref _updateStatus, value);
        }
        public AddonUpdateInfo? UpdateInfo { get; set; }
        public bool CanEnable { get; set; }
        public bool CanDisable { get; set; }
        public bool RequiresServerStopped { get; set; } = true;
        public bool HasVersion => !string.IsNullOrEmpty(Version);
        public string LoaderTypeForUpdate => ApiVersion;
        public string ToggleActionLabel => IsDisabled ? "Enable" : "Disable";
        public string ToggleToolTip => CanEnable || CanDisable
            ? $"{ToggleActionLabel} this plugin"
            : "Stop the server before enabling or disabling mods/plugins.";
        public bool IsEnabled => !IsDisabled;
        public bool CanToggle => CanEnable || CanDisable;
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

        // Extended fields for richer UI
        public string FileName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string? Version { get; set; }
        public string LoaderType { get; set; } = "Unknown";
        public string SourceLabel { get; set; } = "Manual";
        public ImageSource? Icon { get; set; }
        public bool HasWarnings { get; set; }
        public string WarningText { get; set; } = "";
        public bool IsClientOnly { get; set; }
        public bool IsMetadataUnknown { get; set; }
        public bool IsDisabled { get; set; }
        public AddonKind Kind { get; set; } = AddonKind.Mod;
        public AddonState State { get; set; } = AddonState.Enabled;
        private AddonUpdateStatus _updateStatus = AddonUpdateStatus.Unknown;
        public AddonUpdateStatus UpdateStatus
        {
            get => _updateStatus;
            set => SetProperty(ref _updateStatus, value);
        }
        public AddonUpdateInfo? UpdateInfo { get; set; }
        public bool CanEnable { get; set; }
        public bool CanDisable { get; set; }
        public bool RequiresServerStopped { get; set; } = true;
        public bool HasVersion => !string.IsNullOrEmpty(Version);
        public string ToggleActionLabel => IsDisabled ? "Enable" : "Disable";
        public string ToggleToolTip => CanEnable || CanDisable
            ? $"{ToggleActionLabel} this mod"
            : "Stop the server before enabling or disabling mods/plugins.";
        public bool IsEnabled => !IsDisabled;
        public bool CanToggle => CanEnable || CanDisable;

        public string SideLabel { get; set; } = "Unknown";
        public ModSideSupport SideSupport { get; set; }

        public Brush SideBadgeBackground
        {
            get
            {
                string hex = SideSupport switch
                {
                    ModSideSupport.ClientOnly => "#2D1E24",
                    ModSideSupport.ServerOnly => "#1E2838",
                    ModSideSupport.ClientAndServer => "#1E382A",
                    ModSideSupport.OptionalOnServer => "#38351E",
                    _ => "#282828"
                };
                return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            }
        }

        public Brush SideBadgeForeground
        {
            get
            {
                string hex = SideSupport switch
                {
                    ModSideSupport.ClientOnly => "#F38BA8",
                    ModSideSupport.ServerOnly => "#89B4FA",
                    ModSideSupport.ClientAndServer => "#A6E3A1",
                    ModSideSupport.OptionalOnServer => "#F9E2AF",
                    _ => "#A6ADC8"
                };
                return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            }
        }
    }

}
