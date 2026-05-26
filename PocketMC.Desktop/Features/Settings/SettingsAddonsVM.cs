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

            OpenFolderCommand = new RelayCommand(p => OpenContainingFolder(p as string));
            ToggleModActiveCommand = new RelayCommand(async p => await ToggleModActiveAsync(p as string), _ => !_isRunningCheck());
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
                    var pluginItems = BuildJavaPluginList(manifest);
                    var modItems = BuildJavaModList(manifest);
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

        private List<PluginItemViewModel> BuildJavaPluginList(AddonManifest manifest)
        {
            var result = new List<PluginItemViewModel>();
            var dir = System.IO.Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(dir)) return result;

#pragma warning disable CS0618 // PluginScanner is deprecated — retained for Java back-compat only
            foreach (var file in Directory.GetFiles(dir, "*.jar"))
            {
                var fi      = new FileInfo(file);
                var entry = manifest.Entries.FirstOrDefault(e =>
                    e.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                var metadata = JavaModMetadataService.ScanJar(file);

                string api  = PluginScanner.TryGetApiVersion(file) ?? "Unknown";
                string name;
                if (entry != null && !string.IsNullOrEmpty(entry.DisplayName))
                {
                    name = entry.DisplayName;
                }
                else if (entry != null && !string.IsNullOrEmpty(entry.ProjectTitle))
                {
                    name = entry.ProjectTitle;
                }
                else if (!string.IsNullOrEmpty(metadata.DisplayName) && metadata.LoaderType == "Plugin")
                {
                    name = metadata.DisplayName;
                }
                else
                {
                    name = PluginScanner.TryGetPluginName(file) ?? metadata.DisplayName;
                }

                bool bad    = PluginScanner.IsIncompatible(api == "Unknown" ? null : api, _metadata.MinecraftVersion);

                var warnings = new List<string>();
                if (bad)
                {
                    warnings.Add($"Incompatible API version ({api}). Server is running {_metadata.MinecraftVersion}.");
                }
                if (metadata.Warnings.Count > 0)
                {
                    warnings.AddRange(metadata.Warnings);
                }

                ImageSource? icon = AddonIconService.GetIcon(file, "Plugin", metadata.IconBytes);
                string sourceLabel = entry != null ? (entry.Provider ?? "Manual") : "Manual";

                result.Add(new PluginItemViewModel
                {
                    Name = name, Path = file, ApiVersion = api,
                    SizeKb = fi.Length / 1024.0, IsMismatch = bad,
                    LastModified = fi.LastWriteTime,
                    ManifestEntry = entry,
                    FileName = fi.Name,
                    Version = metadata.Version ?? (api != "Unknown" ? api : null),
                    SourceLabel = sourceLabel,
                    Icon = icon,
                    HasWarnings = warnings.Count > 0,
                    WarningText = string.Join(Environment.NewLine, warnings)
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

            var files = Directory.GetFiles(dir, "*.jar")
                .Concat(Directory.GetFiles(dir, "*.jar.disabled"))
                .ToArray();

            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                var entry = manifest.Entries.FirstOrDefault(e =>
                    e.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                var metadata = JavaModMetadataService.ScanJar(file);

                string displayName = "";
                if (!string.IsNullOrEmpty(metadata.DisplayName) && metadata.LoaderType != "Unknown")
                {
                    displayName = metadata.DisplayName;
                }
                else if (entry != null && !string.IsNullOrEmpty(entry.DisplayName))
                {
                    displayName = entry.DisplayName;
                }
                else if (entry != null && !string.IsNullOrEmpty(entry.ProjectTitle))
                {
                    displayName = entry.ProjectTitle;
                }
                else
                {
                    displayName = metadata.DisplayName;
                }

                var warnings = new List<string>(metadata.Warnings);
                ImageSource? icon = AddonIconService.GetIcon(file, metadata.LoaderType, metadata.IconBytes);
                string sourceLabel = entry != null ? (entry.Provider ?? "Manual") : "Manual";

                bool isDisabled = fi.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (isDisabled)
                {
                    warnings.Add("This mod is currently disabled.");
                }

                var sideSupport = metadata.SideSupport;
                if (entry != null && (!string.IsNullOrEmpty(entry.ClientSide) || !string.IsNullOrEmpty(entry.ServerSide)))
                {
                    var providerSide = MapModrinthSide(entry.ClientSide, entry.ServerSide);
                    if (providerSide != ModSideSupport.Unknown)
                    {
                        sideSupport = providerSide;
                    }
                }

                string sideLabel = sideSupport switch
                {
                    ModSideSupport.ClientOnly => "Client-only",
                    ModSideSupport.ServerOnly => "Server-only",
                    ModSideSupport.ClientAndServer => "Client + Server",
                    ModSideSupport.OptionalOnServer => "Optional on server",
                    ModSideSupport.OptionalOnClient => "Optional on client",
                    _ => "Side unknown"
                };

                result.Add(new ModItemViewModel
                {
                    Name = displayName,
                    DisplayName = displayName,
                    FileName = fi.Name,
                    Path = file,
                    SizeKb = fi.Length / 1024.0,
                    LastModified = fi.LastWriteTime,
                    ManifestEntry = entry,
                    Version = metadata.Version,
                    LoaderType = metadata.LoaderType,
                    SourceLabel = sourceLabel,
                    Icon = icon,
                    HasWarnings = warnings.Count > 0,
                    WarningText = string.Join(Environment.NewLine, warnings),
                    SideSupport = sideSupport,
                    SideLabel = sideLabel,
                    IsClientOnly = sideSupport == ModSideSupport.ClientOnly,
                    IsMetadataUnknown = metadata.LoaderType == "Unknown",
                    IsDisabled = isDisabled
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

                    var confirmMessage = BuildUpdateConfirmationMessage(displayName, manifestEntry.VersionId, result.LatestVersionName ?? "Unknown", result.Warnings);
                    var confirm = await _dialogService.ShowDialogAsync(
                        "Update Available",
                        confirmMessage,
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

                    var reinstallMessage = BuildReinstallConfirmationMessage(displayName, result.LatestVersionName ?? "Unknown", result.Warnings);
                    var reinstall = await _dialogService.ShowDialogAsync(
                        "Already Up To Date",
                        reinstallMessage,
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

                var allWarnings = updateResults.SelectMany(u => u.Result.Warnings).Distinct().ToList();
                var updatesList = updateResults.Select(u => (u.Name, LatestVersionName: u.Result.LatestVersionName ?? "Unknown")).ToList();
                var confirmMessage = BuildBatchUpdateSummaryMessage(updateResults.Count, trackedItems.Count, updatesList, allWarnings);

                var confirm = await _dialogService.ShowDialogAsync(
                    "Updates Available",
                    confirmMessage,
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
                    using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { /* Ignore */ }
        }

        internal async Task ToggleModActiveAsync(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_isRunningCheck())
            {
                _dialogService.ShowMessage("Server is Running", "Cannot enable or disable mods while the server is running.", DialogType.Warning);
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(path) ?? "";
                string fileName = Path.GetFileName(path);
                string newPath;
                if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    string activeName = fileName.Substring(0, fileName.Length - ".disabled".Length);
                    newPath = Path.Combine(dir, activeName);
                }
                else
                {
                    newPath = path + ".disabled";
                }

                if (File.Exists(path))
                {
                    File.Move(path, newPath);
                    await _manifestService.UpdateManifestFileNameAsync(_serverDir, Path.GetFileName(path), Path.GetFileName(newPath));
                }
                LoadAddons();
                _onAddonChanged();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", ex.Message, DialogType.Error);
            }
        }

        private static ModSideSupport MapModrinthSide(string? clientSide, string? serverSide)
        {
            if (string.IsNullOrEmpty(clientSide) && string.IsNullOrEmpty(serverSide))
                return ModSideSupport.Unknown;

            string client = clientSide?.ToLowerInvariant() ?? "";
            string server = serverSide?.ToLowerInvariant() ?? "";

            bool clientReqOrSup = client == "required" || client == "supported";
            bool serverReqOrSup = server == "required" || server == "supported";
            bool clientUnsupported = client == "unsupported";
            bool serverUnsupported = server == "unsupported";

            if (clientReqOrSup && serverReqOrSup)
                return ModSideSupport.ClientAndServer;
            
            if (clientReqOrSup && serverUnsupported)
                return ModSideSupport.ClientOnly;
                
            if (clientUnsupported && serverReqOrSup)
                return ModSideSupport.ServerOnly;

            if (server == "optional")
                return ModSideSupport.OptionalOnServer;

            return ModSideSupport.Unknown;
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
        public string? Version { get; set; }
        public string SourceLabel { get; set; } = "Manual";
        public ImageSource? Icon { get; set; }
        public bool HasWarnings { get; set; }
        public string WarningText { get; set; } = "";
        public bool IsDisabled { get; set; }
        public bool HasVersion => !string.IsNullOrEmpty(Version);
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
        public string? Version { get; set; }
        public string LoaderType { get; set; } = "Unknown";
        public string SourceLabel { get; set; } = "Manual";
        public ImageSource? Icon { get; set; }
        public bool HasWarnings { get; set; }
        public string WarningText { get; set; } = "";
        public bool IsClientOnly { get; set; }
        public bool IsMetadataUnknown { get; set; }
        public bool IsDisabled { get; set; }
        public bool HasVersion => !string.IsNullOrEmpty(Version);

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
