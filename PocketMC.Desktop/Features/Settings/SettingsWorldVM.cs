using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Models;
using System;
using System.Diagnostics;
using System.IO;
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
        }

        private async Task UploadWorldAsync()
        {
            string filter = _profile.SupportsBedrockWorlds
                ? "World Files (*.zip;*.mcworld)|*.zip;*.mcworld"
                : "ZIP Files (*.zip)|*.zip";

            var file = await _dialogService.OpenFileDialogAsync("Select World Archive", filter);
            if (file != null)
            {
                try
                {
                    string targetWorldPath = ResolveWorldDir();
                    await _worldManager.ImportWorldZipAsync(file, targetWorldPath, p => { });
                    LoadWorldState();
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
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

        private void BrowseMaps()
        {
            var browserPage = ActivatorUtilities.CreateInstance<MapBrowserPage>(_serviceProvider, _mcVersion);
            browserPage.OnMapDownloaded += async (tempZip) =>
            {
                try
                {
                    string targetWorldPath = ResolveWorldDir();
                    await _worldManager.ImportWorldZipAsync(tempZip, targetWorldPath, p => { });
                    LoadWorldState();
                    File.Delete(tempZip);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Import Failed", ex.Message, DialogType.Error);
                }
            };
            _navigationService.NavigateToDetailPage(browserPage, "Maps", DetailRouteKind.PluginBrowser, DetailBackNavigation.PreviousDetail);
        }
    }
}
