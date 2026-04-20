using System;
using System.Collections.ObjectModel;
using System.IO;
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

        // ── Installed addon collections ──────────────────────────────────
        public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
        public ObservableCollection<ModItemViewModel> Mods { get; } = new();

        // ── Engine predicates ────────────────────────────────────────────
        public bool ShowVanillaWarning   => _metadata.ServerType?.StartsWith("Vanilla",    StringComparison.OrdinalIgnoreCase) == true;
        public bool IsBedrockDedicated  => _metadata.ServerType?.StartsWith("Bedrock",    StringComparison.OrdinalIgnoreCase) == true;
        public bool IsPocketmine        => _metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsBedrockOrPocketmine => IsBedrockDedicated || IsPocketmine;
        /// <summary>True for Java-based engines (Vanilla, Paper, Fabric, Forge).</summary>
        public bool IsJavaEngine => !IsBedrockDedicated && !IsPocketmine;

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

            // Resolve the Bedrock installer from DI (if not Bedrock this is a no-op).
            _bedrockInstaller = serviceProvider.GetRequiredService<BedrockAddonInstaller>();

            // ── Plugin commands — routed by engine ───────────────────────────────────
            // BDS: no plugins concept (addons handled via Mods section below)
            // PocketMine: .phar files via Poggit browser
            // Java: JAR files via local picker
            AddPluginCommand            = new RelayCommand(
                async _ => { if (IsPocketmine) BrowsePoggit(); else await AddPluginAsync(); },
                _ => !_isRunningCheck() && !ShowVanillaWarning && !IsBedrockDedicated);
            DeletePluginCommand         = new RelayCommand(
                async p => await DeletePluginAsync(p as string),
                _ => !_isRunningCheck() && !IsBedrockDedicated);
            BrowseModrinthPluginsCommand = new RelayCommand(
                _ => { if (IsPocketmine) BrowsePoggit(); else BrowseModrinth("project_type:plugin"); },
                _ => !IsBedrockDedicated);

            // ── Mod commands — routed by engine ──────────────────────────────────────
            // BDS: "Add Mod" triggers local .mcpack/.mcaddon import
            // Java: JAR picker
            AddModCommand               = new RelayCommand(
                async _ => { if (IsBedrockDedicated) await ImportBedrockAddonAsync(); else await AddModAsync(); },
                _ => !_isRunningCheck() && !ShowVanillaWarning);
            DeleteModCommand            = new RelayCommand(
                async p => { if (IsBedrockDedicated) await DeleteBedrockAddonAsync(p as string); else await DeleteModAsync(p as string); },
                _ => !_isRunningCheck());
            BrowseModrinthModsCommand   = new RelayCommand(
                _ => { if (IsBedrockDedicated) ImportBedrockAddonCommand?.Execute(null); else BrowseModrinth("project_type:mod"); },
                _ => true);
            ImportModpackCommand        = new RelayCommand(async _ => await ImportModpackAsync(), _ => IsJavaEngine);
            BrowseModpacksCommand       = new RelayCommand(_ => BrowseModrinth("project_type:modpack"), _ => IsJavaEngine);

            // ── Bedrock-specific commands (also reachable via unified commands above) ─
            ImportBedrockAddonCommand   = new RelayCommand(async _ => await ImportBedrockAddonAsync(), _ => IsBedrockDedicated && !_isRunningCheck());
            DeleteBedrockAddonCommand   = new RelayCommand(async p => await DeleteBedrockAddonAsync(p as string), _ => IsBedrockDedicated && !_isRunningCheck());

            // ── PocketMine-specific commands ──────────────────────────────
            BrowsePoggitCommand         = new RelayCommand(_ => BrowsePoggit(), _ => IsPocketmine);
        }

        public void LoadAddons()
        {
            if (IsBedrockDedicated)
                LoadBedrockAddons();
            else if (IsPocketmine)
                LoadPocketminePlugins();
            else
            {
                LoadPlugins();
                LoadMods();
            }
        }

        // ── Bedrock addon management ──────────────────────────────────────

        private void LoadBedrockAddons()
        {
            Mods.Clear();
            var installed = _bedrockInstaller.GetInstalledAddons(_serverDir);
            foreach (var addon in installed)
            {
                Mods.Add(new ModItemViewModel
                {
                    Name         = addon.Name,
                    Path         = addon.FilePath,
                    SizeKb       = addon.SizeKb,
                    LastModified = addon.LastModified,
                    AddonType    = addon.AddonType
                });
            }
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

            LoadBedrockAddons();
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
                LoadBedrockAddons();
                _onAddonChanged();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", ex.Message, DialogType.Error);
            }
        }

        // ── PocketMine plugin management ──────────────────────────────────

        private void LoadPocketminePlugins()
        {
            Plugins.Clear();
            var dir = System.IO.Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.phar"))
            {
                var fi = new FileInfo(file);
                Plugins.Add(new PluginItemViewModel
                {
                    Name         = fi.Name,
                    Path         = file,
                    ApiVersion   = "PocketMine",
                    SizeKb       = fi.Length / 1024.0,
                    IsMismatch   = false,
                    LastModified = fi.LastWriteTime
                });
            }
        }

        private void BrowsePoggit()
        {
            // Open the browser page locked to Poggit as the sole source.
            BrowseModrinthInternal("project_type:plugin", lockToPoggit: true);
        }

        // ── Java plugin / mod management ──────────────────────────────────

        private void LoadPlugins()
        {
            Plugins.Clear();
            var dir = System.IO.Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(dir)) return;

#pragma warning disable CS0618 // PluginScanner is deprecated — retained for Java back-compat only
            foreach (var file in Directory.GetFiles(dir, "*.jar"))
            {
                var fi      = new FileInfo(file);
                string api  = PluginScanner.TryGetApiVersion(file) ?? "Unknown";
                string name = PluginScanner.TryGetPluginName(file) ?? fi.Name;
                bool bad    = PluginScanner.IsIncompatible(api == "Unknown" ? null : api, _metadata.MinecraftVersion);
                Plugins.Add(new PluginItemViewModel { Name = name, Path = file, ApiVersion = api, SizeKb = fi.Length / 1024.0, IsMismatch = bad, LastModified = fi.LastWriteTime });
            }
#pragma warning restore CS0618
        }

        private async Task AddPluginAsync()
        {
            string filter = "JAR Files (*.jar)|*.jar";
            var files = await _dialogService.OpenFilesDialogAsync("Select Plugin(s)", filter);
            foreach (var f in files)
            {
                var dir = System.IO.Path.Combine(_serverDir, "plugins");
                Directory.CreateDirectory(dir);
                await FileUtils.CopyFileAsync(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)), true);
            }
            LoadPlugins(); _onAddonChanged();
        }

        private async Task DeletePluginAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {System.IO.Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try 
                { 
                    await FileUtils.DeleteFileAsync(path); 
                    await _manifestService.UnregisterByFileNameAsync(_serverDir, Path.GetFileName(path));
                    LoadPlugins(); 
                    _onAddonChanged(); 
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private void LoadMods()
        {
            Mods.Clear();
            var dir = System.IO.Path.Combine(_serverDir, "mods");
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.jar"))
            {
                var fi = new FileInfo(file);
                Mods.Add(new ModItemViewModel { Name = fi.Name, Path = file, SizeKb = fi.Length / 1024.0, LastModified = fi.LastWriteTime });
            }
        }

        private async Task AddModAsync()
        {
            var files = await _dialogService.OpenFilesDialogAsync("Select Mod(s)", "JAR Files (*.jar)|*.jar");
            foreach (var f in files)
            {
                var dir = System.IO.Path.Combine(_serverDir, "mods");
                Directory.CreateDirectory(dir);
                await FileUtils.CopyFileAsync(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)), true);
            }
            LoadMods(); _onAddonChanged();
        }

        private async Task DeleteModAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {System.IO.Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try 
                { 
                    await FileUtils.DeleteFileAsync(path); 
                    await _manifestService.UnregisterByFileNameAsync(_serverDir, Path.GetFileName(path));
                    LoadMods(); 
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
                        (Action)(() => { if (projectType.Contains("plugin")) LoadPlugins(); else LoadMods(); _onAddonChanged(); }),
                        _metadata.ServerType ?? ""
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
    }

    // ── View models ───────────────────────────────────────────────────────

    public class PluginItemViewModel
    {
        public string Name        { get; set; } = "";
        public string Path        { get; set; } = "";
        public string ApiVersion  { get; set; } = "";
        public double SizeKb      { get; set; }
        public bool   IsMismatch  { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class ModItemViewModel
    {
        public string Name        { get; set; } = "";
        public string Path        { get; set; } = "";
        public double SizeKb      { get; set; }
        public DateTime LastModified { get; set; }
        /// <summary>"behavior" | "resource" for BDS; empty for Java mods.</summary>
        public string AddonType   { get; set; } = "";
    }
}
