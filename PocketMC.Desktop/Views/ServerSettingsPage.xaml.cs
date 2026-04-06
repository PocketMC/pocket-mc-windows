using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Utils;
using UiButton = Wpf.Ui.Controls.Button;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace PocketMC.Desktop.Views
{
    public class PropertyItem
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
    public partial class ServerSettingsPage : Page
    {
        private readonly InstanceManager _instanceManager;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly PlayitAgentService _playitAgentService;
        private readonly PlayitApiClient _playitApiClient;
        private readonly ILogger<ServerSettingsPage> _logger;
        private InstanceMetadata _metadata;
        private string _serverDir;
        private readonly WorldManager _worldManager;
        private readonly BackupService _backupService;
        private ulong _totalSystemRamMb;
        private bool _hasUnsavedChanges = false;
        private bool _isLoading = false;
        private bool _isSynchronizingTabSelection;
        private readonly MouseWheelEventHandler _settingsPagePreviewMouseWheelHandler;
        private bool _isForwardingMouseWheel;

        private readonly ModpackService _modpackService;
        public ServerSettingsPage(
            InstanceMetadata metadata,
            InstanceManager instanceManager,
            ServerProcessManager serverProcessManager,
            WorldManager worldManager,
            BackupService backupService,
            PlayitAgentService playitAgentService,
            PlayitApiClient playitApiClient,
            ModpackService modpackService,
            ILogger<ServerSettingsPage> logger)
        {
            _modpackService = modpackService;
            InitializeComponent();
            _settingsPagePreviewMouseWheelHandler = OnSettingsPagePreviewMouseWheel;
            _instanceManager = instanceManager;
            _serverProcessManager = serverProcessManager;
            _worldManager = worldManager;
            _backupService = backupService;
            _playitAgentService = playitAgentService;
            _playitApiClient = playitApiClient;
            _logger = logger;
            _metadata = metadata;
            _serverDir = _instanceManager.GetInstancePath(_metadata.Id)
                ?? throw new DirectoryNotFoundException($"Could not locate the server directory for '{_metadata.Name}'.");

            LoadSettings();
            LoadWorldTab();
            LoadPluginTab();
            LoadModTab();
            LoadBackupTab();
            LoadCrashRestartTab();

            Loaded += ServerSettingsPage_Loaded;
            Unloaded += ServerSettingsPage_Unloaded;

            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;

            // Force initial selection (U-FIX: ensures Properties shows up on open)
            SidebarList.SelectedIndex = 0;
            MainTabControl.SelectedIndex = 0;
            RefreshLockStates();

            // Track changes across the full settings page, not just the properties tab.
            AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnTrackedTextChanged), true);
            AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(OnTrackedSelectionChanged), true);
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnTrackedToggleChanged), true);
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnTrackedToggleChanged), true);
            SldMinRam.ValueChanged += OnSettingChanged;
            SldMaxRam.ValueChanged += OnSettingChanged;
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (!_isLoading) _hasUnsavedChanges = true;
        }

        private void ServerSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            AddHandler(UIElement.PreviewMouseWheelEvent, _settingsPagePreviewMouseWheelHandler, true);
            QueueTabTransitionAnimation();
        }

        private void ServerSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            RemoveHandler(UIElement.PreviewMouseWheelEvent, _settingsPagePreviewMouseWheelHandler);
        }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSynchronizingTabSelection)
            {
                return;
            }

            if (SidebarList != null && MainTabControl != null && SidebarList.SelectedIndex != -1)
            {
                MainTabControl.SelectedIndex = SidebarList.SelectedIndex;
            }
        }

        private void OnTrackedTextChanged(object sender, TextChangedEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                OnSettingChanged(sender, e);
            }
        }

        private void OnTrackedSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is ComboBox)
            {
                OnSettingChanged(sender, e);
            }
        }

        private void OnTrackedToggleChanged(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is ToggleButton)
            {
                OnSettingChanged(sender, e);
            }
        }

        private void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl)
            {
                return;
            }

            RefreshLockStates();

            if (SidebarList.SelectedIndex != MainTabControl.SelectedIndex)
            {
                _isSynchronizingTabSelection = true;
                SidebarList.SelectedIndex = MainTabControl.SelectedIndex;
                _isSynchronizingTabSelection = false;
            }

            QueueTabTransitionAnimation();
        }

        private void QueueTabTransitionAnimation()
        {
            if (!IsLoaded)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AnimateContentAreaTransition));
        }

        private void AnimateContentAreaTransition()
        {
            if (ContentAreaCard == null)
            {
                return;
            }

            ContentAreaCard.BeginAnimation(OpacityProperty, null);
            if (ContentAreaCard.RenderTransform is TranslateTransform translateTransform)
            {
                translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                translateTransform.Y = 8;
                translateTransform.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }

            ContentAreaCard.Opacity = 0;
            ContentAreaCard.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void OnSettingsPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_isForwardingMouseWheel || e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            if (FindAncestor<ScrollBar>(source) != null)
            {
                return;
            }

            ComboBox? comboBox = FindAncestor<ComboBox>(source);
            if (comboBox?.IsDropDownOpen == true)
            {
                return;
            }

            ScrollViewer? activeScrollViewer = GetActiveTabScrollViewer();
            if (activeScrollViewer == null || activeScrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            e.Handled = true;

            try
            {
                _isForwardingMouseWheel = true;
                int steps = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine) * 3;
                for (int i = 0; i < steps; i++)
                {
                    if (e.Delta > 0)
                    {
                        activeScrollViewer.LineUp();
                    }
                    else
                    {
                        activeScrollViewer.LineDown();
                    }
                }
            }
            finally
            {
                _isForwardingMouseWheel = false;
            }
        }

        private ScrollViewer? GetActiveTabScrollViewer()
        {
            return MainTabControl.SelectedIndex switch
            {
                0 => PropertiesScrollViewer,
                1 => WorldsScrollViewer,
                2 => PluginsScrollViewer,
                3 => ModsScrollViewer,
                4 => BackupsScrollViewer,
                5 => CrashRestartScrollViewer,
                _ => null
            };
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                DependencyObject? visualParent = null;
                try
                {
                    visualParent = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    // Some popup-hosted elements can throw here; fall back to logical ancestry.
                }

                current = visualParent ?? LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        // ════════════════════════════════════════════════
        //  LOCK STATE (running server protection)
        // ════════════════════════════════════════════════

        private void RefreshLockStates()
        {
            bool isRunning = _serverProcessManager.IsRunning(_metadata.Id);

            // Worlds tab
            TxtWorldLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnUploadWorld.IsEnabled = !isRunning;
            BtnDeleteWorld.IsEnabled = !isRunning;

            // Plugins tab
            TxtPluginLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnAddPlugin.IsEnabled = !isRunning;

            // Mods tab
            TxtModLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            BtnAddMod.IsEnabled = !isRunning;

            // Vanilla warning for plugins
            bool isVanilla = _metadata.ServerType?.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) == true;
            TxtVanillaWarning.Visibility = isVanilla ? Visibility.Visible : Visibility.Collapsed;
            if (isVanilla) BtnAddPlugin.IsEnabled = false;
        }

        // ════════════════════════════════════════════════
        //  TAB 1: PROPERTIES (existing logic preserved)
        // ════════════════════════════════════════════════

        private void LoadSettings()
        {
            _isLoading = true;
            _totalSystemRamMb = MemoryHelper.GetTotalPhysicalMemoryMb();
            double maxAllowedRam = Math.Max(8192.0, (double)_totalSystemRamMb);
            SldMinRam.Maximum = maxAllowedRam;
            SldMaxRam.Maximum = maxAllowedRam;

            SldMinRam.Value = _metadata.MinRamMb > 0 ? _metadata.MinRamMb : 1024;
            SldMaxRam.Value = _metadata.MaxRamMb > 0 ? _metadata.MaxRamMb : 4096;
            TxtJavaPath.Text = _metadata.CustomJavaPath ?? "";
            TxtAdvancedJvmArgs.Text = _metadata.AdvancedJvmArgs ?? "";

            var propsFile = Path.Combine(_serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            TxtMotd.Text = props.TryGetValue("motd", out var motd) ? motd : "A Minecraft Server";
            TxtSeed.Text = props.TryGetValue("level-seed", out var seed) ? seed : "";
            TxtSpawnProtection.Text = props.TryGetValue("spawn-protection", out var prot) ? prot : "16";
            TxtMaxPlayers.Text = props.TryGetValue("max-players", out var mp) ? mp : "20";

            // Load Advanced Properties
            var namedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "motd", "level-seed", "spawn-protection", "max-players", "server-port", "server-ip",
                "level-type", "online-mode", "pvp", "white-list", "gamemode", "difficulty",
                "enable-command-block", "allow-flight", "allow-nether"
            };

            var advancedItems = new ObservableCollection<PropertyItem>(
                props.Where(kvp => !namedKeys.Contains(kvp.Key))
                     .Select(kvp => new PropertyItem { Key = kvp.Key, Value = kvp.Value })
            );
            AdvancedPropsGrid.ItemsSource = advancedItems;
            
            string portString = props.TryGetValue("server-port", out var port) ? port : "25565";
            TxtServerPort.Text = portString;
            
            TxtServerIp.Text = props.TryGetValue("server-ip", out var ip) ? ip : "";

            if (props.TryGetValue("level-type", out var lt))
            {
                foreach (ComboBoxItem item in CmbLevelType.Items)
                {
                    if (item.Content.ToString() == lt)
                        CmbLevelType.SelectedItem = item;
                }
            }

            if (props.TryGetValue("online-mode", out var om)) ChkOnlineMode.IsChecked = om == "true";
            if (props.TryGetValue("pvp", out var pvp)) ChkPvp.IsChecked = pvp == "true";
            if (props.TryGetValue("white-list", out var wl)) ChkWhiteList.IsChecked = wl == "true";

            if (props.TryGetValue("gamemode", out var gm))
            {
                foreach (ComboBoxItem item in CmbGamemode.Items)
                {
                    if (item.Content.ToString() == gm)
                        CmbGamemode.SelectedItem = item;
                }
            }

            if (props.TryGetValue("difficulty", out var dif))
            {
                foreach (ComboBoxItem item in CmbDifficulty.Items)
                {
                    if (item.Content.ToString() == dif)
                        CmbDifficulty.SelectedItem = item;
                }
            }

            if (props.TryGetValue("enable-command-block", out var cb)) ChkAllowBlock.IsChecked = cb == "true";
            if (props.TryGetValue("allow-flight", out var af)) ChkAllowFlight.IsChecked = af == "true";
            if (props.TryGetValue("allow-nether", out var an)) ChkAllowNether.IsChecked = an == "true";

            var iconPath = Path.Combine(_serverDir, "server-icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ImgIconPreview.Source = bmp;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load the server icon from {IconPath}.", iconPath);
                }
            }

            // Update Playit Agent Status
            if (_playitAgentService.State != PlayitAgentState.Stopped)
            {
                TxtPlayitAgentStatus.Text = _playitAgentService.State.ToString();
                TxtPlayitAgentStatus.Foreground = _playitAgentService.State == PlayitAgentState.Connected ? Brushes.LimeGreen : Brushes.Orange;
            }
            else
            {
                TxtPlayitAgentStatus.Text = "Not initialized";
                TxtPlayitAgentStatus.Foreground = Brushes.Gray;
            }

            // Fire and forget tunnel resolution
            _ = ResolveTunnelAddressAsync(portString);

            _isLoading = false;
            _hasUnsavedChanges = false;
        }

        private void BtnResolvePlayit_Click(object sender, RoutedEventArgs e)
        {
            _ = ResolveTunnelAddressAsync(TxtServerPort.Text);
        }

        private void BtnOpenPlayitDashboard_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://playit.gg/account/tunnels", UseShellExecute = true });
        }

        private async System.Threading.Tasks.Task ResolveTunnelAddressAsync(string portString)
        {
            if (!int.TryParse(portString, out int port))
            {
                TxtPlayitAddress.Text = "Invalid port configured.";
                return;
            }

            TxtPlayitAddress.Text = "Resolving tunnel for port " + port + "...";
            try
            {
                var result = await _playitApiClient.GetTunnelsAsync();

                if (!result.Success)
                {
                    TxtPlayitAddress.Text = result.RequiresClaim
                        ? "⚠️ Setup still pending. Finish the Playit claim flow from Tunnel."
                        : result.IsTokenInvalid
                            ? "⚠️ Token invalid. Re-link account via Tunnel."
                            : "⚠️ API unreachable.";
                    return;
                }

                var match = PlayitApiClient.FindTunnelForPort(result.Tunnels, port);
                if (match != null)
                {
                    TxtPlayitAddress.Text = match.PublicAddress;
                }
                else
                {
                    TxtPlayitAddress.Text = "No tunnel linked for this port. Start the server to create one.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve the Playit tunnel address for {ServerName}.", _metadata.Name);
                TxtPlayitAddress.Text = "Failed to resolve tunnel.";
            }
        }

        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            OnSettingChanged(sender, e);
            if (TxtRamWarning == null) return;
            if (_totalSystemRamMb > 0)
            {
                double totalRequested = SldMaxRam.Value;
                TxtRamWarning.Visibility = totalRequested > (_totalSystemRamMb * 0.8) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TxtMotd_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtMotdPreview.Text = TxtMotd.Text;
        }

        private async void BtnBrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PNG Files (*.png)|*.png",
                Title = "Select Server Icon (Must be 64x64)"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dlg.FileName);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    if (bmp.PixelWidth != 64 || bmp.PixelHeight != 64)
                    {
                        MessageBox.Show("Icon must be exactly 64x64 pixels.", "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    ImgIconPreview.Source = bmp;
                    var dest = Path.Combine(_serverDir, "server-icon.png");
                    await FileUtils.CopyFileAsync(dlg.FileName, dest, overwrite: true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading image: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _logger.LogWarning(ex, "Failed to import the server icon from {SourceFile}.", dlg.FileName);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to discard them?",
                    "Discard Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No) return;
            }

            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.NavigateBackFromDetail() == true)
            {
                return;
            }

            if (NavigationService?.CanGoBack == true)
            {
                NavigationService.GoBack();
                return;
            }
        }

        private void BtnBrowseJava_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Java Runtime (java.exe)|java.exe|Executables (*.exe)|*.exe",
                Title = "Select Java Runtime Executable"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtJavaPath.Text = openFileDialog.FileName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _metadata.MinRamMb = (int)SldMinRam.Value;
            _metadata.MaxRamMb = (int)SldMaxRam.Value;
            
            _metadata.EnableAutoRestart = ChkEnableAutoRestart.IsChecked == true;
            if (int.TryParse(TxtMaxAutoRestarts.Text, out int m)) _metadata.MaxAutoRestarts = m;
            if (int.TryParse(TxtAutoRestartDelay.Text, out int d)) _metadata.AutoRestartDelaySeconds = d;
            _metadata.CustomJavaPath = string.IsNullOrWhiteSpace(TxtJavaPath.Text) ? null : TxtJavaPath.Text;
            _metadata.AdvancedJvmArgs = string.IsNullOrWhiteSpace(TxtAdvancedJvmArgs.Text) ? null : TxtAdvancedJvmArgs.Text.Trim();
            _instanceManager.SaveMetadata(_metadata, _serverDir);

            var propsFile = Path.Combine(_serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            props["motd"] = TxtMotd.Text;
            if (!string.IsNullOrWhiteSpace(TxtSeed.Text)) props["level-seed"] = TxtSeed.Text;
            props["spawn-protection"] = TxtSpawnProtection.Text;
            props["max-players"] = TxtMaxPlayers.Text;
            props["server-port"] = TxtServerPort.Text;
            if (!string.IsNullOrWhiteSpace(TxtServerIp.Text)) props["server-ip"] = TxtServerIp.Text;
            else props.Remove("server-ip");

            props["level-type"] = (CmbLevelType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "minecraft:normal";
            props["online-mode"] = ChkOnlineMode.IsChecked == true ? "true" : "false";
            props["pvp"] = ChkPvp.IsChecked == true ? "true" : "false";
            props["white-list"] = ChkWhiteList.IsChecked == true ? "true" : "false";
            props["gamemode"] = (CmbGamemode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "survival";
            props["difficulty"] = (CmbDifficulty.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "easy";

            props["enable-command-block"] = ChkAllowBlock.IsChecked == true ? "true" : "false";
            props["allow-flight"] = ChkAllowFlight.IsChecked == true ? "true" : "false";
            props["allow-nether"] = ChkAllowNether.IsChecked == true ? "true" : "false";

            // Merge Advanced Properties (Syncs exactly what is in the Grid)
            var namedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "motd", "level-seed", "spawn-protection", "max-players", "server-port", "server-ip",
                "level-type", "online-mode", "pvp", "white-list", "gamemode", "difficulty",
                "enable-command-block", "allow-flight", "allow-nether"
            };

            // Remove existing custom keys that aren't mapped to specific UI controls to handle deletions
            var keysToRemove = props.Keys.Where(k => !namedKeys.Contains(k)).ToList();
            foreach (var k in keysToRemove) props.Remove(k);

            if (AdvancedPropsGrid.ItemsSource is IEnumerable<PropertyItem> advancedItems)
            {
                foreach (var item in advancedItems)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key))
                    {
                        props[item.Key] = item.Value;
                    }
                }
            }

            ServerPropertiesParser.Write(propsFile, props);

            _hasUnsavedChanges = false;
            
            // Visual confirmation instead of MessageBox (D-04)
            string originalText = BtnSave.Content.ToString() ?? "Save Configurations";
            BtnSave.Content = "✓ Saved";
            
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, ev) => 
            { 
                BtnSave.Content = originalText; 
                timer.Stop(); 
            };
            timer.Start();
        }

        // ════════════════════════════════════════════════
        //  TAB 2: WORLDS
        // ════════════════════════════════════════════════

        private void LoadWorldTab()
        {
            var worldDir = Path.Combine(_serverDir, "world");
            if (Directory.Exists(worldDir))
            {
                TxtWorldStatus.Text = "✅ World folder exists";
                double sizeMb = FileUtils.GetDirectorySizeMb(worldDir);
                TxtWorldSize.Text = $"Size: {sizeMb} MB";
            }
            else
            {
                TxtWorldStatus.Text = "No world folder found (will be generated on first start)";
                TxtWorldSize.Text = "";
            }
            RefreshLockStates();
        }

        private async void BtnUploadWorld_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "ZIP Files (*.zip)|*.zip",
                Title = "Select World ZIP"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    BtnUploadWorld.IsEnabled = false;
                    BtnDeleteWorld.IsEnabled = false;
                    TxtWorldProgress.Visibility = Visibility.Visible;

                    var targetWorldPath = Path.Combine(_serverDir, "world");
                    await _worldManager.ImportWorldZipAsync(dlg.FileName, targetWorldPath, progress =>
                    {
                        Dispatcher.Invoke(() => TxtWorldProgress.Text = progress);
                    });

                    LoadWorldTab();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"World import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    TxtWorldProgress.Visibility = Visibility.Collapsed;
                    RefreshLockStates();
                }
            }
        }

        private async void BtnDeleteWorld_Click(object sender, RoutedEventArgs e)
        {
            var worldDir = Path.Combine(_serverDir, "world");
            if (!Directory.Exists(worldDir))
            {
                MessageBox.Show("No world folder found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to delete the current world? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await FileUtils.CleanDirectoryAsync(worldDir);
                    LoadWorldTab();
                    MessageBox.Show("World deleted successfully.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete world: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ════════════════════════════════════════════════
        //  TAB 3: PLUGINS
        // ════════════════════════════════════════════════

        private void LoadPluginTab()
        {
            var pluginsDir = Path.Combine(_serverDir, "plugins");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
            }

            PluginList.Items.Clear();
            var jars = Directory.GetFiles(pluginsDir, "*.jar");

            foreach (var jar in jars)
            {
                var fi = new FileInfo(jar);
                string apiVersion = PluginScanner.TryGetApiVersion(jar) ?? "Unknown";
                string pluginName = PluginScanner.TryGetPluginName(jar) ?? fi.Name;

                // Only flag as incompatible if the plugin requires a NEWER API
                // than the server provides. Backward compat is guaranteed by Spigot/Paper:
                // e.g. api-version 1.14 on server 1.20.4 = FINE (backward compatible)
                // e.g. api-version 1.21 on server 1.20.4 = MISMATCH (too new)
                bool mismatch = PluginScanner.IsIncompatible(apiVersion == "Unknown" ? null : apiVersion, _metadata.MinecraftVersion);

                var row = CreateSettingsItemCard();

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoPanel = new StackPanel();
                var nameText = new TextBlock
                {
                    Text = pluginName + (mismatch ? "  ⚠ Version Mismatch" : ""),
                    Foreground = mismatch ? Brushes.Orange : GetThemeBrush("TextFillColorPrimaryBrush", Colors.White),
                    FontWeight = FontWeights.SemiBold
                };
                var detailText = new TextBlock
                {
                    Text = $"Size: {Math.Round(fi.Length / 1024.0, 1)} KB  |  API: {apiVersion}  |  Modified: {fi.LastWriteTime:yyyy-MM-dd}",
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Colors.Silver),
                    FontSize = 11
                };
                infoPanel.Children.Add(nameText);
                infoPanel.Children.Add(detailText);
                Grid.SetColumn(infoPanel, 0);
                grid.Children.Add(infoPanel);

                var deleteBtn = CreateActionButton("Remove", ControlAppearance.Danger, jar, !_serverProcessManager.IsRunning(_metadata.Id));
                deleteBtn.Click += DeletePlugin_Click;
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);

                row.Child = grid;
                PluginList.Items.Add(row);
            }

            if (jars.Length == 0)
            {
                PluginList.Items.Add(CreateEmptyStateText("No plugins installed."));
            }

            RefreshLockStates();
        }

        private async void BtnAddPlugin_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JAR Files (*.jar)|*.jar",
                Title = "Select Plugin JAR",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                var pluginsDir = Path.Combine(_serverDir, "plugins");
                foreach (var file in dlg.FileNames)
                {
                    var dest = Path.Combine(pluginsDir, Path.GetFileName(file));
                    await FileUtils.CopyFileAsync(file, dest, overwrite: true);
                }
                LoadPluginTab();
            }
        }

        private async void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var result = MessageBox.Show(
                    $"Delete plugin '{Path.GetFileName(path)}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        await FileUtils.DeleteFileAsync(path);
                        LoadPluginTab();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _logger.LogWarning(ex, "Failed to delete plugin {PluginPath}.", path);
                    }
                }
            }
        }

        // ════════════════════════════════════════════════
        //  TAB 4: MODS
        // ════════════════════════════════════════════════

        private void BtnBrowseModrinth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string projectType)
            {
                var browserPage = new PluginBrowserPage(_serverDir, _metadata.MinecraftVersion, projectType, () =>
                {
                    if (projectType.Contains("plugin")) LoadPluginTab();
                    else LoadModTab();
                });

                if (projectType == "project_type:modpack")
                {
                    browserPage.OnModpackDownloaded += async (tempZip) =>
                    {
                        await ImportModpackAsync(tempZip);
                        try { File.Delete(tempZip); } catch { }
                    };
                }

                NavigationService.Navigate(browserPage);
            }
        }

        private void LoadModTab()
        {
            var modsDir = Path.Combine(_serverDir, "mods");
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
            }

            ModList.Items.Clear();
            var jars = Directory.GetFiles(modsDir, "*.jar");

            foreach (var jar in jars)
            {
                var fi = new FileInfo(jar);

                var row = CreateSettingsItemCard();

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoPanel = new StackPanel();
                infoPanel.Children.Add(new TextBlock
                {
                    Text = fi.Name,
                    Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Colors.White),
                    FontWeight = FontWeights.SemiBold
                });
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"Size: {Math.Round(fi.Length / 1024.0, 1)} KB  |  Modified: {fi.LastWriteTime:yyyy-MM-dd}",
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Colors.Silver),
                    FontSize = 11
                });
                Grid.SetColumn(infoPanel, 0);
                grid.Children.Add(infoPanel);

                var deleteBtn = CreateActionButton("Remove", ControlAppearance.Danger, jar, !_serverProcessManager.IsRunning(_metadata.Id));
                deleteBtn.Click += DeleteMod_Click;
                Grid.SetColumn(deleteBtn, 1);
                grid.Children.Add(deleteBtn);

                row.Child = grid;
                ModList.Items.Add(row);
            }

            if (jars.Length == 0)
            {
                ModList.Items.Add(CreateEmptyStateText("No mods installed."));
            }

            RefreshLockStates();
        }

        private async void BtnAddMod_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JAR Files (*.jar)|*.jar",
                Title = "Select Mod JAR",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                var modsDir = Path.Combine(_serverDir, "mods");
                foreach (var file in dlg.FileNames)
                {
                    var dest = Path.Combine(modsDir, Path.GetFileName(file));
                    await FileUtils.CopyFileAsync(file, dest, overwrite: true);
                }
                LoadModTab();
            }
        }

        private async void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var result = MessageBox.Show(
                    $"Delete mod '{Path.GetFileName(path)}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        await FileUtils.DeleteFileAsync(path);
                        LoadModTab();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _logger.LogWarning(ex, "Failed to delete mod {ModPath}.", path);
                    }
                }
            }
        }

        // ════════════════════════════════════════════════
        //  TAB 5: BACKUPS
        // ════════════════════════════════════════════════


        private async void BtnImportModpack_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modpack ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
                Title = "Select Modpack ZIP"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await ImportModpackAsync(openFileDialog.FileName);
            }
        }

        private async Task ImportModpackAsync(string zipPath)
        {
            try
            {
                var result = await _modpackService.ParseModpackZipAsync(zipPath);

                var confirm = System.Windows.MessageBox.Show(
                    $"Import modpack '{result.Name}' for Minecraft {result.MinecraftVersion} ({result.Loader}) to this server?",
                    "Import Modpack",
                    MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    await _modpackService.ImportToExistingInstanceAsync(result, _metadata, _serverDir, zipPath);
                    LoadSettings(); // Reload properties
                    LoadModTab();
                    System.Windows.MessageBox.Show("Modpack imported successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import modpack from {ZipPath}.", zipPath);
                System.Windows.MessageBox.Show($"Failed to import modpack: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadBackupTab()
        {
            // Set schedule dropdown to current value
            foreach (ComboBoxItem item in CmbBackupInterval.Items)
            {
                if (item.Tag?.ToString() == _metadata.BackupIntervalHours.ToString())
                {
                    CmbBackupInterval.SelectedItem = item;
                    break;
                }
            }

            // Set max backups dropdown to current value
            foreach (ComboBoxItem item in CmbMaxBackups.Items)
            {
                if (item.Tag?.ToString() == _metadata.MaxBackupsToKeep.ToString())
                {
                    CmbMaxBackups.SelectedItem = item;
                    break;
                }
            }

            // Show restore lock warning if running
            bool isRunning = _serverProcessManager.IsRunning(_metadata.Id);
            TxtBackupLockWarning.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

            // Load backup list
            RefreshBackupList();
        }

        private void RefreshBackupList()
        {
            BackupList.Items.Clear();
            var backupDir = Path.Combine(_serverDir, "backups");
            if (!Directory.Exists(backupDir))
            {
                BackupList.Items.Add(CreateEmptyStateText("No backups yet."));
                return;
            }

            var files = new DirectoryInfo(backupDir)
                .GetFiles("world-*.zip")
                .OrderByDescending(f => f.CreationTime)
                .ToArray();

            if (files.Length == 0)
            {
                BackupList.Items.Add(CreateEmptyStateText("No backups yet."));
                return;
            }

            bool isRunning = _serverProcessManager.IsRunning(_metadata.Id);

            foreach (var file in files)
            {
                var row = CreateSettingsItemCard();

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = file.Name,
                    Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Colors.White),
                    FontWeight = FontWeights.SemiBold
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"Size: {Math.Round(file.Length / (1024.0 * 1024.0), 1)} MB  |  Created: {file.CreationTime:yyyy-MM-dd HH:mm}",
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Colors.Silver),
                    FontSize = 11
                });
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var restoreBtn = CreateActionButton("Restore", ControlAppearance.Primary, file.FullName, !isRunning, new Thickness(0, 0, 6, 0));
                restoreBtn.Click += RestoreBackup_Click;
                Grid.SetColumn(restoreBtn, 1);
                grid.Children.Add(restoreBtn);

                var deleteBtn = CreateActionButton("Delete", ControlAppearance.Danger, file.FullName);
                deleteBtn.Click += DeleteBackup_Click;
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);

                row.Child = grid;
                BackupList.Items.Add(row);
            }
        }

        private async void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnCreateBackup.IsEnabled = false;
                TxtBackupProgress.Visibility = Visibility.Visible;

                await _backupService.RunBackupAsync(_metadata, _serverDir, progress =>
                {
                    Dispatcher.Invoke(() => TxtBackupProgress.Text = progress);
                });

                RefreshBackupList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Backup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCreateBackup.IsEnabled = true;
                TxtBackupProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_serverProcessManager.IsRunning(_metadata.Id))
            {
                MessageBox.Show("Stop the server before restoring a backup.", "Server Running", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is Button btn && btn.Tag is string zipPath)
            {
                var result = MessageBox.Show(
                    $"Restore '{Path.GetFileName(zipPath)}'? This will REPLACE the current world.",
                    "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        TxtBackupProgress.Visibility = Visibility.Visible;
                        await _backupService.RestoreBackupAsync(zipPath, _serverDir, progress =>
                        {
                            Dispatcher.Invoke(() => TxtBackupProgress.Text = progress);
                        });
                        MessageBox.Show("World restored successfully!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Restore failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        TxtBackupProgress.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var result = MessageBox.Show(
                    $"Delete backup '{Path.GetFileName(path)}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        await FileUtils.DeleteFileAsync(path);
                        RefreshBackupList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _logger.LogWarning(ex, "Failed to delete backup archive {BackupPath}.", path);
                    }
                }
            }
        }

        private void CmbBackupInterval_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbBackupInterval.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                if (int.TryParse(item.Tag.ToString(), out int hours))
                {
                    _metadata.BackupIntervalHours = hours;
                    SaveMetadata();
                }
            }
        }

        private void CmbMaxBackups_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbMaxBackups.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                if (int.TryParse(item.Tag.ToString(), out int max))
                {
                    _metadata.MaxBackupsToKeep = max;
                    SaveMetadata();
                }
            }
        }

        private void SaveMetadata()
        {
            _instanceManager.SaveMetadata(_metadata, _serverDir);
        }

        // ════════════════════════════════════════════════
        //  TAB 6: CRASH & RESTART
        // ════════════════════════════════════════════════

        private void LoadCrashRestartTab()
        {
            ChkEnableAutoRestart.IsChecked = _metadata.EnableAutoRestart;
            TxtMaxAutoRestarts.Text = _metadata.MaxAutoRestarts.ToString();
            TxtAutoRestartDelay.Text = _metadata.AutoRestartDelaySeconds.ToString();
        }

        private Border CreateSettingsItemCard()
        {
            return new Border
            {
                Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", Color.FromRgb(0x2D, 0x33, 0x3B)),
                BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromRgb(0x49, 0x4F, 0x58)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private UiButton CreateActionButton(string content, ControlAppearance appearance, object? tag, bool isEnabled = true, Thickness? margin = null)
        {
            return new UiButton
            {
                Content = content,
                Appearance = appearance,
                Padding = new Thickness(12, 6, 12, 6),
                MinWidth = 84,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin ?? new Thickness(0),
                Tag = tag,
                IsEnabled = isEnabled
            };
        }

        private TextBlock CreateEmptyStateText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Colors.Gray),
                FontStyle = FontStyles.Italic
            };
        }

        private Brush GetThemeBrush(string resourceKey, Color fallbackColor)
        {
            return TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallbackColor);
        }
    }
}
