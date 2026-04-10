using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Utils;
using PocketMC.Desktop.Views;

namespace PocketMC.Desktop.ViewModels
{
    public class ServerSettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly InstanceManager _instanceManager;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly WorldManager _worldManager;
        private readonly BackupService _backupService;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly ModpackService _modpackService;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly IAppDispatcher _dispatcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ServerSettingsViewModel> _logger;
        private readonly Action<Guid, ServerState> _instanceStateChangedHandler;

        public InstanceMetadata Metadata { get; }
        public string ServerDir { get; }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges { get => _hasUnsavedChanges; set => SetProperty(ref _hasUnsavedChanges, value); }

        // Properties
        private string? _motd;
        public string? Motd { get => _motd; set { if (SetProperty(ref _motd, value)) MarkChanged(); } }

        private string? _javaPath;
        public string? JavaPath { get => _javaPath; set { if (SetProperty(ref _javaPath, value)) MarkChanged(); } }

        private string? _advancedJvmArgs;
        public string? AdvancedJvmArgs { get => _advancedJvmArgs; set { if (SetProperty(ref _advancedJvmArgs, value)) MarkChanged(); } }

        private BitmapImage? _serverIcon;
        public BitmapImage? ServerIcon { get => _serverIcon; set => SetProperty(ref _serverIcon, value); }

        private double _minRam = 1024;
        public double MinRam { get => _minRam; set { if (SetProperty(ref _minRam, value)) { MarkChanged(); OnPropertyChanged(nameof(MinRamDisplay)); CheckRamWarning(); } } }
        public string MinRamDisplay => $"{MinRam:N0} MB";

        private double _maxRam = 4096;
        public double MaxRam { get => _maxRam; set { if (SetProperty(ref _maxRam, value)) { MarkChanged(); OnPropertyChanged(nameof(MaxRamDisplay)); CheckRamWarning(); } } }
        public string MaxRamDisplay => $"{MaxRam:N0} MB";

        private bool _showRamWarning;
        public bool ShowRamWarning { get => _showRamWarning; set => SetProperty(ref _showRamWarning, value); }
        private readonly double _totalRamMb;

        // World
        private string? _seed;
        public string? Seed { get => _seed; set { if (SetProperty(ref _seed, value)) MarkChanged(); } }

        private string _levelType = "minecraft:normal";
        public string LevelType { get => _levelType; set { if (SetProperty(ref _levelType, value)) MarkChanged(); } }
        public string[] LevelTypes { get; } = { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "minecraft:single_biome_surface" };

        private string _spawnProtection = "16";
        public string SpawnProtection { get => _spawnProtection; set { if (SetProperty(ref _spawnProtection, value)) MarkChanged(); } }

        // Players
        private string _maxPlayers = "20";
        public string MaxPlayers { get => _maxPlayers; set { if (SetProperty(ref _maxPlayers, value)) MarkChanged(); } }

        private bool _onlineMode = true;
        public bool OnlineMode { get => _onlineMode; set { if (SetProperty(ref _onlineMode, value)) MarkChanged(); } }

        private bool _pvp = true;
        public bool Pvp { get => _pvp; set { if (SetProperty(ref _pvp, value)) MarkChanged(); } }

        private bool _whiteList = false;
        public bool WhiteList { get => _whiteList; set { if (SetProperty(ref _whiteList, value)) MarkChanged(); } }

        // Network
        private string _serverPort = "25565";
        public string ServerPort { get => _serverPort; set { if (SetProperty(ref _serverPort, value)) MarkChanged(); } }

        private string? _serverIp;
        public string? ServerIp { get => _serverIp; set { if (SetProperty(ref _serverIp, value)) MarkChanged(); } }

        private string _playitAgentStatusText = "Checking...";
        public string PlayitAgentStatusText { get => _playitAgentStatusText; set => SetProperty(ref _playitAgentStatusText, value); }

        private string _playitAddress = "Resolving tunnel from agent...";
        public string PlayitAddress { get => _playitAddress; set => SetProperty(ref _playitAddress, value); }

        // Gameplay
        private string _gamemode = "survival";
        public string Gamemode { get => _gamemode; set { if (SetProperty(ref _gamemode, value)) MarkChanged(); } }
        public string[] Gamemodes { get; } = { "survival", "creative", "adventure", "spectator" };

        private string _difficulty = "easy";
        public string Difficulty { get => _difficulty; set { if (SetProperty(ref _difficulty, value)) MarkChanged(); } }
        public string[] Difficulties { get; } = { "peaceful", "easy", "normal", "hard" };

        private bool _allowBlock = false;
        public bool AllowBlock { get => _allowBlock; set { if (SetProperty(ref _allowBlock, value)) MarkChanged(); } }

        private bool _allowFlight = false;
        public bool AllowFlight { get => _allowFlight; set { if (SetProperty(ref _allowFlight, value)) MarkChanged(); } }

        private bool _allowNether = true;
        public bool AllowNether { get => _allowNether; set { if (SetProperty(ref _allowNether, value)) MarkChanged(); } }

        public ObservableCollection<PropertyItem> AdvancedProperties { get; } = new();

        // Crash
        private bool _enableAutoRestart = false;
        public bool EnableAutoRestart { get => _enableAutoRestart; set { if (SetProperty(ref _enableAutoRestart, value)) MarkChanged(); } }

        private string _maxAutoRestarts = "3";
        public string MaxAutoRestarts { get => _maxAutoRestarts; set { if (SetProperty(ref _maxAutoRestarts, value)) MarkChanged(); } }

        private string _autoRestartDelay = "10";
        public string AutoRestartDelay { get => _autoRestartDelay; set { if (SetProperty(ref _autoRestartDelay, value)) MarkChanged(); } }

        // Worlds Tab
        private string _worldStatusText = "Checking world...";
        public string WorldStatusText { get => _worldStatusText; set => SetProperty(ref _worldStatusText, value); }

        private string _worldSizeText = "";
        public string WorldSizeText { get => _worldSizeText; set => SetProperty(ref _worldSizeText, value); }

        private string _worldProgressText = "";
        public string WorldProgressText { get => _worldProgressText; set => SetProperty(ref _worldProgressText, value); }
        private bool _showWorldProgress = false;
        public bool ShowWorldProgress { get => _showWorldProgress; set => SetProperty(ref _showWorldProgress, value); }

        // Collections
        public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
        public ObservableCollection<ModItemViewModel> Mods { get; } = new();
        public ObservableCollection<BackupItemViewModel> Backups { get; } = new();

        public bool ShowVanillaWarning => Metadata.ServerType?.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) == true;

        private int _backupIntervalHours = 0;
        public int BackupIntervalHours { get => _backupIntervalHours; set { if (SetProperty(ref _backupIntervalHours, value)) SaveBackupSettings(); } }

        private int _maxBackupsToKeep = 10;
        public int MaxBackupsToKeep { get => _maxBackupsToKeep; set { if (SetProperty(ref _maxBackupsToKeep, value)) SaveBackupSettings(); } }

        private string _backupProgressText = "";
        public string BackupProgressText { get => _backupProgressText; set => SetProperty(ref _backupProgressText, value); }
        private bool _showBackupProgress = false;
        public bool ShowBackupProgress { get => _showBackupProgress; set => SetProperty(ref _showBackupProgress, value); }

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BrowseIconCommand { get; }
        public ICommand BrowseJavaCommand { get; }
        public ICommand ResolvePlayitCommand { get; }
        public ICommand OpenPlayitDashboardCommand { get; }

        public ICommand UploadWorldCommand { get; }
        public ICommand DeleteWorldCommand { get; }

        public ICommand AddPluginCommand { get; }
        public ICommand DeletePluginCommand { get; }
        public ICommand BrowseModrinthPluginsCommand { get; }

        public ICommand AddModCommand { get; }
        public ICommand DeleteModCommand { get; }
        public ICommand BrowseModrinthModsCommand { get; }
        public ICommand ImportModpackCommand { get; }
        public ICommand BrowseModpacksCommand { get; }

        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }

        public ServerSettingsViewModel(
            InstanceMetadata metadata,
            InstanceManager instanceManager,
            ServerProcessManager serverProcessManager,
            WorldManager worldManager,
            BackupService backupService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            ModpackService modpackService,
            IDialogService dialogService,
            IAppNavigationService navigationService,
            IAppDispatcher dispatcher,
            IServiceProvider serviceProvider,
            ILogger<ServerSettingsViewModel> logger)
        {
            Metadata = metadata;
            _instanceManager = instanceManager;
            _serverProcessManager = serverProcessManager;
            _worldManager = worldManager;
            _backupService = backupService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _modpackService = modpackService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _dispatcher = dispatcher;
            _serviceProvider = serviceProvider;
            _logger = logger;
            ServerDir = _instanceManager.GetInstancePath(metadata.Id) ?? throw new InvalidOperationException();
            _totalRamMb = MemoryHelper.GetTotalPhysicalMemoryMb();
            _instanceStateChangedHandler = OnInstanceStateChanged;
            _serverProcessManager.OnInstanceStateChanged += _instanceStateChangedHandler;

            SaveCommand = new RelayCommand(_ => SaveConfigurations());
            CancelCommand = new RelayCommand(async _ => await CancelAsync());
            BrowseIconCommand = new RelayCommand(async _ => await BrowseIconAsync());
            BrowseJavaCommand = new RelayCommand(async _ => await BrowseJavaAsync());
            ResolvePlayitCommand = new RelayCommand(_ => _ = ResolveTunnelAddressAsync());
            OpenPlayitDashboardCommand = new RelayCommand(_ => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://playit.gg/account/tunnels", UseShellExecute = true }));

            UploadWorldCommand = new RelayCommand(async _ => await UploadWorldAsync(), _ => !IsRunning);
            DeleteWorldCommand = new RelayCommand(async _ => await DeleteWorldAsync(), _ => !IsRunning);

            AddPluginCommand = new RelayCommand(async _ => await AddPluginAsync(), _ => !IsRunning && !ShowVanillaWarning);
            DeletePluginCommand = new RelayCommand(async p => await DeletePluginAsync(p as string), _ => !IsRunning);
            BrowseModrinthPluginsCommand = new RelayCommand(_ => BrowseModrinth("project_type:plugin"));

            AddModCommand = new RelayCommand(async _ => await AddModAsync(), _ => !IsRunning);
            DeleteModCommand = new RelayCommand(async p => await DeleteModAsync(p as string), _ => !IsRunning);
            BrowseModrinthModsCommand = new RelayCommand(_ => BrowseModrinth("project_type:mod"));

            ImportModpackCommand = new RelayCommand(async _ => await ImportModpackAsync());
            BrowseModpacksCommand = new RelayCommand(_ => BrowseModrinth("project_type:modpack"));

            CreateBackupCommand = new RelayCommand(async _ => await CreateBackupAsync());
            RestoreBackupCommand = new RelayCommand(async p => await RestoreBackupAsync(p as string), _ => !IsRunning);
            DeleteBackupCommand = new RelayCommand(async p => await DeleteBackupAsync(p as string));

            LoadAll();
        }

        public void LoadAll()
        {
            IsLoading = true;
            UpdateRunningState();

            // Load Properties
            MinRam = Metadata.MinRamMb > 0 ? Metadata.MinRamMb : 1024;
            MaxRam = Metadata.MaxRamMb > 0 ? Metadata.MaxRamMb : 4096;
            JavaPath = Metadata.CustomJavaPath;
            AdvancedJvmArgs = Metadata.AdvancedJvmArgs;
            EnableAutoRestart = Metadata.EnableAutoRestart;
            MaxAutoRestarts = Metadata.MaxAutoRestarts.ToString();
            AutoRestartDelay = Metadata.AutoRestartDelaySeconds.ToString();

            BackupIntervalHours = Metadata.BackupIntervalHours;
            MaxBackupsToKeep = Metadata.MaxBackupsToKeep;

            var propsFile = Path.Combine(ServerDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            Motd = props.TryGetValue("motd", out var motd) ? motd : "A Minecraft Server";
            Seed = props.TryGetValue("level-seed", out var seed) ? seed : "";
            SpawnProtection = props.TryGetValue("spawn-protection", out var prot) ? prot : "16";
            MaxPlayers = props.TryGetValue("max-players", out var mp) ? mp : "20";
            ServerPort = props.TryGetValue("server-port", out var port) ? port : "25565";
            ServerIp = props.TryGetValue("server-ip", out var ip) ? ip : "";
            LevelType = props.TryGetValue("level-type", out var lt) ? lt : "minecraft:normal";
            OnlineMode = props.TryGetValue("online-mode", out var om) && om == "true";
            Pvp = props.TryGetValue("pvp", out var pvp) ? (pvp == "true") : true;
            WhiteList = props.TryGetValue("white-list", out var wl) && wl == "true";
            Gamemode = props.TryGetValue("gamemode", out var gm) ? gm : "survival";
            Difficulty = props.TryGetValue("difficulty", out var dif) ? dif : "easy";
            AllowBlock = props.TryGetValue("enable-command-block", out var cb) && cb == "true";
            AllowFlight = props.TryGetValue("allow-flight", out var af) && af == "true";
            AllowNether = props.TryGetValue("allow-nether", out var an) ? (an == "true") : true;

            var namedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "motd", "level-seed", "spawn-protection", "max-players", "server-port", "server-ip",
                "level-type", "online-mode", "pvp", "white-list", "gamemode", "difficulty",
                "enable-command-block", "allow-flight", "allow-nether"
            };

            AdvancedProperties.Clear();
            foreach (var kvp in props.Where(k => !namedKeys.Contains(k.Key)))
            {
                var item = new PropertyItem { Key = kvp.Key, Value = kvp.Value };
                item.PropertyChanged += (s, e) => MarkChanged();
                AdvancedProperties.Add(item);
            }
            AdvancedProperties.CollectionChanged += (s, e) => MarkChanged();

            LoadIcon();
            LoadWorldTab();
            LoadPlugins();
            LoadMods();
            LoadBackups();

            PlayitAgentStatusText = _playitAgentService.State.ToString();
            _ = ResolveTunnelAddressAsync();

            IsLoading = false;
            HasUnsavedChanges = false;
        }

        private void OnInstanceStateChanged(Guid instanceId, ServerState state)
        {
            if (instanceId != Metadata.Id)
            {
                return;
            }

            _dispatcher.Invoke(UpdateRunningState);
        }

        private void UpdateRunningState()
        {
            IsRunning = _serverProcessManager.IsRunning(Metadata.Id);
            CommandManager.InvalidateRequerySuggested();
        }

        private void MarkChanged()
        {
            if (!IsLoading) HasUnsavedChanges = true;
        }

        private void CheckRamWarning()
        {
            if (_totalRamMb > 0)
                ShowRamWarning = MaxRam > (_totalRamMb * 0.8);
        }

        private void LoadIcon()
        {
            var iconPath = Path.Combine(ServerDir, "server-icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ServerIcon = bmp;
                }
                catch { ServerIcon = null; }
            }
            else { ServerIcon = null; }
        }

        private async Task BrowseIconAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Server Icon (Must be 64x64 PNG)", "PNG Files (*.png)|*.png");
            if (file != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(file);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    if (bmp.PixelWidth != 64 || bmp.PixelHeight != 64)
                    {
                        _dialogService.ShowMessage("Invalid Size", "Icon must be exactly 64x64 pixels.", DialogType.Warning);
                        return;
                    }

                    var dest = Path.Combine(ServerDir, "server-icon.png");
                    File.Copy(file, dest, true);
                    ServerIcon = bmp;
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private async Task BrowseJavaAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Java Runtime Executable", "Java Runtime (java.exe)|java.exe|Executables (*.exe)|*.exe");
            if (file != null) JavaPath = file;
        }

        private async Task ResolveTunnelAddressAsync()
        {
            if (!int.TryParse(ServerPort, out int port))
            {
                PlayitAddress = "Invalid port configured.";
                return;
            }

            PlayitAddress = "Resolving tunnel...";
            try
            {
                var result = await _playitApiClient.GetTunnelsAsync();
                if (!result.Success)
                {
                    PlayitAddress = "API unreachable or setup pending.";
                    return;
                }

                var match = PlayitApiClient.FindTunnelForPort(result.Tunnels, port);
                PlayitAddress = match != null ? match.PublicAddress : "No tunnel linked for this port.";
            }
            catch
            {
                PlayitAddress = "Failed to resolve tunnel.";
            }
        }

        private void SaveConfigurations()
        {
            Metadata.MinRamMb = (int)MinRam;
            Metadata.MaxRamMb = (int)MaxRam;
            Metadata.EnableAutoRestart = EnableAutoRestart;
            if (int.TryParse(MaxAutoRestarts, out int m)) Metadata.MaxAutoRestarts = m;
            if (int.TryParse(AutoRestartDelay, out int d)) Metadata.AutoRestartDelaySeconds = d;
            Metadata.CustomJavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath;
            Metadata.AdvancedJvmArgs = string.IsNullOrWhiteSpace(AdvancedJvmArgs) ? null : AdvancedJvmArgs.Trim();

            _instanceManager.SaveMetadata(Metadata, ServerDir);

            var propsFile = Path.Combine(ServerDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            props["motd"] = Motd ?? "";
            if (!string.IsNullOrWhiteSpace(Seed)) props["level-seed"] = Seed;
            props["spawn-protection"] = SpawnProtection;
            props["max-players"] = MaxPlayers;
            props["server-port"] = ServerPort;
            if (!string.IsNullOrWhiteSpace(ServerIp)) props["server-ip"] = ServerIp;
            else props.Remove("server-ip");

            props["level-type"] = LevelType;
            props["online-mode"] = OnlineMode ? "true" : "false";
            props["pvp"] = Pvp ? "true" : "false";
            props["white-list"] = WhiteList ? "true" : "false";
            props["gamemode"] = Gamemode;
            props["difficulty"] = Difficulty;
            props["enable-command-block"] = AllowBlock ? "true" : "false";
            props["allow-flight"] = AllowFlight ? "true" : "false";
            props["allow-nether"] = AllowNether ? "true" : "false";

            var namedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "motd", "level-seed", "spawn-protection", "max-players", "server-port", "server-ip",
                "level-type", "online-mode", "pvp", "white-list", "gamemode", "difficulty",
                "enable-command-block", "allow-flight", "allow-nether"
            };

            var keysToRemove = props.Keys.Where(k => !namedKeys.Contains(k)).ToList();
            foreach (var k in keysToRemove) props.Remove(k);

            foreach (var item in AdvancedProperties)
            {
                if (!string.IsNullOrWhiteSpace(item.Key)) props[item.Key] = item.Value;
            }

            ServerPropertiesParser.Write(propsFile, props);
            HasUnsavedChanges = false;

            _dialogService.ShowMessage("Saved", "Configurations saved successfully.");
        }

        private async Task CancelAsync()
        {
            if (HasUnsavedChanges)
            {
                var result = await _dialogService.ShowDialogAsync("Discard Changes", "You have unsaved changes. Discard them?", DialogType.Warning, false);
                if (result != DialogResult.Yes) return;
            }
            _navigationService.NavigateBack();
        }

        private void LoadWorldTab()
        {
            var worldDir = Path.Combine(ServerDir, "world");
            if (Directory.Exists(worldDir))
            {
                WorldStatusText = "✅ World folder exists";
                WorldSizeText = $"Size: {PocketMC.Desktop.Utils.FileUtils.GetDirectorySizeMb(worldDir)} MB";
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
                ShowWorldProgress = true;
                try
                {
                    await _worldManager.ImportWorldZipAsync(file, Path.Combine(ServerDir, "world"), p => _dispatcher.Invoke(() => WorldProgressText = p));
                    LoadWorldTab();
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
                finally { ShowWorldProgress = false; }
            }
        }

        private async Task DeleteWorldAsync()
        {
            var worldDir = Path.Combine(ServerDir, "world");
            if (!Directory.Exists(worldDir)) return;
            if (await _dialogService.ShowDialogAsync("Confirm", "Delete current world? Cannot be undone.", DialogType.Warning) == DialogResult.Yes)
            {
                try { await PocketMC.Desktop.Utils.FileUtils.CleanDirectoryAsync(worldDir); LoadWorldTab(); }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private void LoadPlugins()
        {
            Plugins.Clear();
            var dir = Path.Combine(ServerDir, "plugins");
            if (!Directory.Exists(dir)) return;
            foreach (var jar in Directory.GetFiles(dir, "*.jar"))
            {
                var fi = new FileInfo(jar);
                string api = PluginScanner.TryGetApiVersion(jar) ?? "Unknown";
                string name = PluginScanner.TryGetPluginName(jar) ?? fi.Name;
                bool mismatch = PluginScanner.IsIncompatible(api == "Unknown" ? null : api, Metadata.MinecraftVersion);

                Plugins.Add(new PluginItemViewModel { Name = name, Path = jar, ApiVersion = api, SizeKb = fi.Length / 1024.0, IsMismatch = mismatch, LastModified = fi.LastWriteTime });
            }
        }

        private async Task AddPluginAsync()
        {
            var files = await _dialogService.OpenFilesDialogAsync("Select Plugin JAR(s)", "JAR Files (*.jar)|*.jar");
            var dir = Path.Combine(ServerDir, "plugins");
            Directory.CreateDirectory(dir);
            foreach (var f in files) await PocketMC.Desktop.Utils.FileUtils.CopyFileAsync(f, Path.Combine(dir, Path.GetFileName(f)), true);
            LoadPlugins();
        }

        private async Task DeletePluginAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try
                {
                    await FileUtils.DeleteFileAsync(path);
                    LoadPlugins();
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Error", ex.Message, DialogType.Error);
                }
            }
        }

        private void LoadMods()
        {
            Mods.Clear();
            var dir = Path.Combine(ServerDir, "mods");
            if (!Directory.Exists(dir)) return;
            foreach (var jar in Directory.GetFiles(dir, "*.jar"))
            {
                var fi = new FileInfo(jar);
                Mods.Add(new ModItemViewModel { Name = fi.Name, Path = jar, SizeKb = fi.Length / 1024.0, LastModified = fi.LastWriteTime });
            }
        }

        private async Task AddModAsync()
        {
            var files = await _dialogService.OpenFilesDialogAsync("Select Mod JAR(s)", "JAR Files (*.jar)|*.jar");
            var dir = Path.Combine(ServerDir, "mods");
            Directory.CreateDirectory(dir);
            foreach (var f in files) await PocketMC.Desktop.Utils.FileUtils.CopyFileAsync(f, Path.Combine(dir, Path.GetFileName(f)), true);
            LoadMods();
        }

        private async Task DeleteModAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete {Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                try
                {
                    await FileUtils.DeleteFileAsync(path);
                    LoadMods();
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Error", ex.Message, DialogType.Error);
                }
            }
        }

        public void Dispose()
        {
            _serverProcessManager.OnInstanceStateChanged -= _instanceStateChangedHandler;
        }

        private void BrowseModrinth(string projectType)
        {
            var browserPage = new PluginBrowserPage(ServerDir, Metadata.MinecraftVersion, projectType, () =>
            {
                if (projectType.Contains("plugin")) LoadPlugins();
                else LoadMods();
            });

            if (projectType == "project_type:modpack")
            {
                browserPage.OnModpackDownloaded += async (tempZip) =>
                {
                    await ImportModpackActionAsync(tempZip);
                    try { File.Delete(tempZip); } catch { }
                };
            }

            _navigationService.NavigateToDetailPage(browserPage, "Marketplace");
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
                if (await _dialogService.ShowDialogAsync("Import Modpack", $"Import modpack '{result.Name}' for Minecraft {result.MinecraftVersion} ({result.Loader}) to this server?", DialogType.Question) == DialogResult.Yes)
                {
                    await _modpackService.ImportToExistingInstanceAsync(result, Metadata, ServerDir, zipPath);
                    LoadAll();
                    _dialogService.ShowMessage("Success", "Modpack imported successfully.");
                }
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
        }

        private void LoadBackups()
        {
            Backups.Clear();
            var dir = Path.Combine(ServerDir, "backups");
            if (!Directory.Exists(dir)) return;
            foreach (var file in new DirectoryInfo(dir).GetFiles("world-*.zip").OrderByDescending(f => f.CreationTime))
            {
                Backups.Add(new BackupItemViewModel { Name = file.Name, Path = file.FullName, SizeMb = file.Length / (1024.0 * 1024.0), Created = file.CreationTime });
            }
        }

        private async Task CreateBackupAsync()
        {
            ShowBackupProgress = true;
            try
            {
                await _backupService.RunBackupAsync(Metadata, ServerDir, p => _dispatcher.Invoke(() => BackupProgressText = p));
                LoadBackups();
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            finally { ShowBackupProgress = false; }
        }

        private async Task RestoreBackupAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm Restore", "Restore this backup? Current world will be REPLACED.", DialogType.Warning) == DialogResult.Yes)
            {
                ShowBackupProgress = true;
                try
                {
                    await _backupService.RestoreBackupAsync(path, ServerDir, p => _dispatcher.Invoke(() => BackupProgressText = p));
                    _dialogService.ShowMessage("Success", "World restored.");
                    LoadWorldTab();
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
                finally { ShowBackupProgress = false; }
            }
        }

        private async Task DeleteBackupAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", $"Delete backup {Path.GetFileName(path)}?", DialogType.Question) == DialogResult.Yes)
            {
                File.Delete(path);
                LoadBackups();
            }
        }

        private void SaveBackupSettings()
        {
            Metadata.BackupIntervalHours = BackupIntervalHours;
            Metadata.MaxBackupsToKeep = MaxBackupsToKeep;
            _instanceManager.SaveMetadata(Metadata, ServerDir);
        }
    }

    public class PluginItemViewModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ApiVersion { get; set; } = "";
        public double SizeKb { get; set; }
        public bool IsMismatch { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class ModItemViewModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public double SizeKb { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class BackupItemViewModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public double SizeMb { get; set; }
        public DateTime Created { get; set; }
    }

    public class PropertyItem : ViewModelBase
    {
        private string _key = "";
        public string Key { get => _key; set => SetProperty(ref _key, value); }
        private string _value = "";
        public string Value { get => _value; set => SetProperty(ref _value, value); }
    }
}
