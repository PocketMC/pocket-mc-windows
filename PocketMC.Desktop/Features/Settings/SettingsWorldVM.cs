using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;

using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Mods;
using System.Linq;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsWorldVM : ViewModelBase
    {
        private string _serverDir;
        private readonly WorldManager _worldManager;
        private readonly ServerConfigurationService _configService;
        private readonly InstanceMetadata _metadata;
        private readonly IDialogService _dialogService;
        private readonly IAppDispatcher _dispatcher;
        private readonly Func<bool> _isRunningCheck;
        private readonly Action _markDirty;
        private readonly ServerSettingsProfile _profile;

        public void UpdateServerDir(string newDir) => _serverDir = newDir;

        // Properties from ServerSettingsViewModel
        private string? _seed;
        public string? Seed { get => _seed; set { if (SetProperty(ref _seed, value)) _markDirty(); } }

        private string _spawnProtection = "16";
        public string SpawnProtection { get => _spawnProtection; set { if (SetProperty(ref _spawnProtection, value)) _markDirty(); } }

        private string _maxPlayers = "20";
        public string MaxPlayers { get => _maxPlayers; set { if (SetProperty(ref _maxPlayers, value)) _markDirty(); } }

        private string _levelType = "minecraft:normal";
        public string LevelType { get => _levelType; set { if (SetProperty(ref _levelType, value)) _markDirty(); } }
        public string[] LevelTypes { get; }

        private string _gamemode = "survival";
        public string Gamemode { get => _gamemode; set { if (SetProperty(ref _gamemode, value)) _markDirty(); } }
        public string[] Gamemodes { get; }

        private string _difficulty = "easy";
        public string Difficulty { get => _difficulty; set { if (SetProperty(ref _difficulty, value)) _markDirty(); } }
        public string[] Difficulties { get; }

        private bool _pvp = true;
        public bool Pvp { get => _pvp; set { if (SetProperty(ref _pvp, value)) _markDirty(); } }

        private bool _whiteList = false;
        public bool WhiteList { get => _whiteList; set { if (SetProperty(ref _whiteList, value)) _markDirty(); } }

        private bool _onlineMode = true;
        public bool OnlineMode { get => _onlineMode; set { if (SetProperty(ref _onlineMode, value)) _markDirty(); } }

        private bool _allowCommandBlock = false;
        public bool AllowCommandBlock { get => _allowCommandBlock; set { if (SetProperty(ref _allowCommandBlock, value)) _markDirty(); } }

        private bool _allowFlight = false;
        public bool AllowFlight { get => _allowFlight; set { if (SetProperty(ref _allowFlight, value)) _markDirty(); } }

        private bool _allowNether = true;
        public bool AllowNether { get => _allowNether; set { if (SetProperty(ref _allowNether, value)) _markDirty(); } }

        private int _viewDistance = 10;
        public int ViewDistance
        {
            get => _viewDistance;
            set { if (SetProperty(ref _viewDistance, Math.Clamp(value, 2, 64))) _markDirty(); }
        }

        private int _simulationDistance = 10;
        public int SimulationDistance
        {
            get => _simulationDistance;
            set { if (SetProperty(ref _simulationDistance, Math.Clamp(value, 2, 64))) _markDirty(); }
        }

        // Existing World logic
        private string _worldStatusText = "Checking world...";
        public string WorldStatusText { get => _worldStatusText; set => SetProperty(ref _worldStatusText, value); }

        private string _worldSizeText = "";
        public string WorldSizeText { get => _worldSizeText; set => SetProperty(ref _worldSizeText, value); }

        /// <summary>
        /// Dynamic button label: includes .mcworld for Bedrock engines.
        /// </summary>
        public string ImportWorldButtonText => _profile.SupportsBedrockWorlds
            ? "Import World (ZIP / .mcworld)"
            : "Import World (ZIP)";

        public ICommand UploadWorldCommand { get; }
        public ICommand DeleteWorldCommand { get; }
        public ICommand BrowseMapsCommand { get; }
        public ICommand BrowseDataPacksCommand { get; }
        public ICommand BrowseResourcePacksCommand { get; }

        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _mcVersion;

        public SettingsWorldVM(
            string serverDir,
            WorldManager worldManager,
            ServerConfigurationService configService,
            InstanceMetadata metadata,
            IDialogService dialogService,
            IAppDispatcher dispatcher,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            string mcVersion,
            ServerSettingsProfile profile,
            Func<bool> isRunningCheck,
            Action markDirty)
        {
            _serverDir = serverDir;
            _worldManager = worldManager;
            _configService = configService;
            _metadata = metadata;
            _dialogService = dialogService;
            _dispatcher = dispatcher;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _mcVersion = mcVersion;
            _profile = profile;
            _isRunningCheck = isRunningCheck;
            _markDirty = markDirty;
            LevelTypes = profile.LevelTypes;
            Gamemodes = profile.Gamemodes;
            Difficulties = profile.Difficulties;

            UploadWorldCommand = new RelayCommand(async _ => await UploadWorldAsync(), _ => !_isRunningCheck());
            DeleteWorldCommand = new RelayCommand(async _ => await DeleteWorldAsync(), _ => !_isRunningCheck());
            BrowseMapsCommand = new RelayCommand(_ => BrowseMaps());
            BrowseDataPacksCommand = new RelayCommand(_ => BrowseDataPacks());
            BrowseResourcePacksCommand = new RelayCommand(_ => BrowseResourcePacks());
            ClearResourcePackCommand = new RelayCommand(_ => ClearResourcePack());
        }

        /// <summary>
        /// Resolves the correct world directory path for the current engine family.
        /// </summary>
        private string ResolveWorldDir()
        {
            return WorldPathResolver.Resolve(_serverDir, _metadata, _configService);
        }

        public void LoadWorldState()
        {
            var worldDir = ResolveWorldDir();
            if (Directory.Exists(worldDir))
            {
                WorldStatusText = "✅ World folder exists";
                WorldSizeText = $"Size: {PocketMC.Domain.Storage.FileUtils.GetDirectorySizeMb(worldDir)} MB";
            }
            else
            {
                WorldStatusText = "No world folder found (will be generated)";
                WorldSizeText = "";
            }

            if (!_profile.SupportsBedrockWorlds && _metadata.Compatibility.Family != EngineFamily.Pocketmine)
                _ = LoadDataPacksAsync();
        }

        private async Task UploadWorldAsync()
        {
            string filter = _profile.SupportsBedrockWorlds
                ? "World Files (*.zip;*.mcworld)|*.zip;*.mcworld"
                : "ZIP Files (*.zip)|*.zip";

            var file = await _dialogService.OpenFileDialogAsync("Select World Archive", filter);
            if (!string.IsNullOrEmpty(file))
            {
                try
                {
                    string targetWorldPath = ResolveWorldDir();
                    string fileName = Path.GetFileName(file);

                    await _dialogService.ShowProgressDialogAsync(
                        "Importing World",
                        $"Importing {fileName}...",
                        async (progress) =>
                        {
                            await _worldManager.ImportWorldZipAsync(
                                file,
                                targetWorldPath,
                                (pct, msg) => progress.Report(new ProgressDialogUpdate
                                {
                                    Percentage = pct,
                                    Message = msg
                                }));
                        });

                    LoadWorldState();
                    _dialogService.ShowMessage("World Imported", $"Successfully imported '{fileName}' into your world folder.", DialogType.Information);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Import Failed", ex.Message, DialogType.Error);
                }
            }
        }

        private async Task DeleteWorldAsync()
        {
            var worldDir = ResolveWorldDir();
            if (!Directory.Exists(worldDir)) return;
            if (await _dialogService.ShowDialogAsync("Confirm", "Delete current world? Cannot be undone.", DialogType.Warning) == DialogResult.Yes)
            {
                try { await PocketMC.Domain.Storage.FileUtils.CleanDirectoryAsync(worldDir); LoadWorldState(); }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private void BrowseDataPacks()
        {
            var page = ActivatorUtilities.CreateInstance<MapBrowserPage>(_serviceProvider, _mcVersion, MarketplacePackKind.DataPack);
            page.OnDataPackSelected += async (provider, projectId) =>
            {
                try
                {
                    if (_isRunningCheck()) throw new InvalidOperationException("Stop the server before changing data packs.");
                    string worldDir = ResolveWorldDir();
                    string name = "";
                    await _dialogService.ShowProgressDialogAsync(
                        "Installing Data Pack",
                        "Downloading data pack...",
                        async (IProgress<ProgressDialogUpdate> progress) =>
                        {
                            name = await _dataPacks.InstallAsync(_serverDir, worldDir, provider, projectId, _mcVersion,
                                new Progress<DownloadProgress>(p => progress.Report(new ProgressDialogUpdate { Percentage = p.Percentage, Message = "Downloading data pack..." })));
                        });
                    await LoadDataPacksAsync();
                    _dialogService.ShowMessage("Data Pack Installed", string.Format("Installed '{0}'. Start the server to apply it.", name), DialogType.Information);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Install Failed", ex.Message, DialogType.Error);
                }
            };
            _navigationService.NavigateToDetailPage(page, "Data Packs", DetailRouteKind.PluginBrowser, DetailBackNavigation.PreviousDetail);
        }

        private void BrowseResourcePacks()
        {
            var page = ActivatorUtilities.CreateInstance<MapBrowserPage>(_serviceProvider, _mcVersion, MarketplacePackKind.ResourcePack);
            page.OnResourcePackSelected += (url, sha1) =>
            {
                try
                {
                    if (_isRunningCheck()) throw new InvalidOperationException("Stop the server before changing its resource pack.");
                    // Java servers push a resource pack to players by URL, so we point server.properties at it.
                    SetResourcePack(url, sha1 ?? "");
                    _dialogService.ShowMessage("Resource Pack Set", "server.properties now points at this resource pack. Start the server to apply it.", DialogType.Information);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Install Failed", ex.Message, DialogType.Error);
                }
            };
            _navigationService.NavigateToDetailPage(page, "Resource Packs", DetailRouteKind.PluginBrowser, DetailBackNavigation.PreviousDetail);
        }

        // ---- Installed pack management ----
        public ObservableCollection<DataPackItemViewModel> DataPacks { get; } = new();
        public ICommand ClearResourcePackCommand { get; private set; } = null!;

        private string _resourcePackText = "";
        public string ResourcePackText { get => _resourcePackText; private set { if (SetProperty(ref _resourcePackText, value)) OnPropertyChanged(nameof(HasResourcePack)); } }
        public bool HasResourcePack => !string.IsNullOrEmpty(_resourcePackText);
        public bool HasDataPacks => DataPacks.Count > 0;

        private DataPackInstallService _dataPacks => _serviceProvider.GetRequiredService<DataPackInstallService>();

        private string PropertiesPath => Path.Combine(_serverDir, "server.properties");

        private void SetResourcePack(string url, string sha1)
        {
            var props = ServerPropertiesParser.Read(PropertiesPath);
            props["resource-pack"] = url;
            props["resource-pack-sha1"] = sha1;
            ServerPropertiesParser.Write(PropertiesPath, props);
            LoadResourcePack();
        }

        public void LoadResourcePack()
        {
            var props = ServerPropertiesParser.Read(PropertiesPath);
            ResourcePackText = props.GetValueOrDefault("resource-pack")?.Trim() ?? "";
        }

        private void ClearResourcePack()
        {
            try
            {
                if (_isRunningCheck()) throw new InvalidOperationException("Stop the server before changing its resource pack.");
                SetResourcePack("", "");
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
        }

        public async Task LoadDataPacksAsync()
        {
            LoadResourcePack();
            DataPacks.Clear();
            string worldDir = ResolveWorldDir();
            foreach (var entry in DataPackLibrary.List(worldDir))
            {
                var item = new DataPackItemViewModel(entry);
                item.ToggleCommand = new RelayCommand(async _ => await RunPackActionAsync(() => DataPackLibrary.SetEnabled(worldDir, entry.Name, !entry.IsEnabled)));
                item.RemoveCommand = new RelayCommand(async _ =>
                {
                    if (await _dialogService.ShowDialogAsync("Confirm", string.Format("Remove data pack '{0}'?", entry.Name), DialogType.Warning) == DialogResult.Yes)
                        await RunPackActionAsync(() => DataPackLibrary.Remove(worldDir, entry.Name));
                });
                item.UpdateCommand = new RelayCommand(async _ =>
                {
                    if (item.Update == null) return;
                    try
                    {
                        if (_isRunningCheck()) throw new InvalidOperationException("Stop the server before updating data packs.");
                        await _dialogService.ShowProgressDialogAsync("Updating Data Pack", entry.Name,
                            async (IProgress<ProgressDialogUpdate> progress) =>
                                await _dataPacks.UpdateAsync(_serverDir, worldDir, entry.Name, item.Update, _mcVersion,
                                    new Progress<DownloadProgress>(p => progress.Report(new ProgressDialogUpdate { Percentage = p.Percentage, Message = entry.Name }))));
                        await LoadDataPacksAsync();
                    }
                    catch (Exception ex) { _dialogService.ShowMessage("Update Failed", ex.Message, DialogType.Error); }
                });
                DataPacks.Add(item);
            }
            OnPropertyChanged(nameof(HasDataPacks));

            // Update checks hit the network, so they run after the list is shown.
            foreach (var item in DataPacks.ToList())
            {
                try { item.Update = await _dataPacks.CheckUpdateAsync(_serverDir, item.Name, _mcVersion); }
                catch { /* offline or provider error: leave the pack without an update badge */ }
            }
        }

        private async Task RunPackActionAsync(Action action)
        {
            try
            {
                if (_isRunningCheck()) throw new InvalidOperationException("Stop the server before changing data packs.");
                action();
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            await LoadDataPacksAsync();
        }

        private void BrowseMaps()
        {
            var browserPage = ActivatorUtilities.CreateInstance<MapBrowserPage>(_serviceProvider, _mcVersion);
            browserPage.OnMapDownloaded += async (tempZip) =>
            {
                try
                {
                    string targetWorldPath = ResolveWorldDir();
                    string fileName = Path.GetFileName(tempZip);

                    await _dialogService.ShowProgressDialogAsync(
                        "Importing World",
                        $"Installing {fileName}...",
                        async (progress) =>
                        {
                            await _worldManager.ImportWorldZipAsync(
                                tempZip,
                                targetWorldPath,
                                (pct, msg) => progress.Report(new ProgressDialogUpdate
                                {
                                    Percentage = pct,
                                    Message = msg
                                }));
                        });

                    LoadWorldState();
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Import Failed", ex.Message, DialogType.Error);
                }
                finally
                {
                    if (File.Exists(tempZip))
                    {
                        try { File.Delete(tempZip); } catch { }
                    }
                }
            };
            _navigationService.NavigateToDetailPage(browserPage, "Maps", DetailRouteKind.PluginBrowser, DetailBackNavigation.PreviousDetail);
        }
    }
}
