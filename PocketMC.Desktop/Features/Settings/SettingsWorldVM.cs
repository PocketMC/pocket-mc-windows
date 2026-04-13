using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Marketplace;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsWorldVM : ViewModelBase
    {
        private readonly string _serverDir;
        private readonly WorldManager _worldManager;
        private readonly IDialogService _dialogService;
        private readonly IAppDispatcher _dispatcher;
        private readonly Func<bool> _isRunningCheck;
        private readonly Action _markDirty;

        // Properties from ServerSettingsViewModel
        private string? _seed;
        public string? Seed { get => _seed; set { if (SetProperty(ref _seed, value)) _markDirty(); } }

        private string _spawnProtection = "16";
        public string SpawnProtection { get => _spawnProtection; set { if (SetProperty(ref _spawnProtection, value)) _markDirty(); } }

        private string _maxPlayers = "20";
        public string MaxPlayers { get => _maxPlayers; set { if (SetProperty(ref _maxPlayers, value)) _markDirty(); } }

        private string _levelType = "minecraft:normal";
        public string LevelType { get => _levelType; set { if (SetProperty(ref _levelType, value)) _markDirty(); } }
        public string[] LevelTypes { get; } = { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "minecraft:single_biome_surface" };

        private string _gamemode = "survival";
        public string Gamemode { get => _gamemode; set { if (SetProperty(ref _gamemode, value)) _markDirty(); } }
        public string[] Gamemodes { get; } = { "survival", "creative", "adventure", "spectator" };

        private string _difficulty = "easy";
        public string Difficulty { get => _difficulty; set { if (SetProperty(ref _difficulty, value)) _markDirty(); } }
        public string[] Difficulties { get; } = { "peaceful", "easy", "normal", "hard" };

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

        // Existing World logic
        private string _worldStatusText = "Checking world...";
        public string WorldStatusText { get => _worldStatusText; set => SetProperty(ref _worldStatusText, value); }

        private string _worldSizeText = "";
        public string WorldSizeText { get => _worldSizeText; set => SetProperty(ref _worldSizeText, value); }

        public ICommand UploadWorldCommand { get; }
        public ICommand DeleteWorldCommand { get; }
        public ICommand BrowseMapsCommand { get; }

        private readonly IAppNavigationService _navigationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _mcVersion;

        public SettingsWorldVM(
            string serverDir,
            WorldManager worldManager,
            IDialogService dialogService,
            IAppDispatcher dispatcher,
            IAppNavigationService navigationService,
            IServiceProvider serviceProvider,
            string mcVersion,
            Func<bool> isRunningCheck,
            Action markDirty)
        {
            _serverDir = serverDir;
            _worldManager = worldManager;
            _dialogService = dialogService;
            _dispatcher = dispatcher;
            _navigationService = navigationService;
            _serviceProvider = serviceProvider;
            _mcVersion = mcVersion;
            _isRunningCheck = isRunningCheck;
            _markDirty = markDirty;

            UploadWorldCommand = new RelayCommand(async _ => await UploadWorldAsync(), _ => !_isRunningCheck());
            DeleteWorldCommand = new RelayCommand(async _ => await DeleteWorldAsync(), _ => !_isRunningCheck());
            BrowseMapsCommand = new RelayCommand(_ => BrowseMaps());
        }

        public void LoadWorldState()
        {
            var worldDir = Path.Combine(_serverDir, "world");
            if (Directory.Exists(worldDir))
            {
                WorldStatusText = "✅ World folder exists";
                WorldSizeText = $"Size: {PocketMC.Desktop.Infrastructure.FileSystem.FileUtils.GetDirectorySizeMb(worldDir)} MB";
            }
            else
            {
                WorldStatusText = "No world folder found (will be generated)";
                WorldSizeText = "";
            }
        }

        private async Task UploadWorldAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select World ZIP", "ZIP Files (*.zip)|*.zip");
            if (file != null)
            {
                try
                {
                    await _worldManager.ImportWorldZipAsync(file, Path.Combine(_serverDir, "world"), p => { });
                    LoadWorldState();
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private async Task DeleteWorldAsync()
        {
            var worldDir = Path.Combine(_serverDir, "world");
            if (!Directory.Exists(worldDir)) return;
            if (await _dialogService.ShowDialogAsync("Confirm", "Delete current world? Cannot be undone.", DialogType.Warning) == DialogResult.Yes)
            {
                try { await PocketMC.Desktop.Infrastructure.FileSystem.FileUtils.CleanDirectoryAsync(worldDir); LoadWorldState(); }
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
                    await _worldManager.ImportWorldZipAsync(tempZip, Path.Combine(_serverDir, "world"), p => { });
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
