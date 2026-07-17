using PocketMC.Application.Services.Setup;
using PocketMC.Desktop.Core.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Infrastructure.Tunnel;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Application.Interfaces;
using PocketMC.RemoteControl.Services;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Application.Interfaces.AI;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.OS;
using PocketMC.Infrastructure.Power;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Infrastructure.Diagnostics;

namespace PocketMC.Desktop.Features.Setup
{
    public class DependencyHealthItem
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public System.Windows.Media.Brush ColorBrush { get; set; } = System.Windows.Media.Brushes.White;
        public string Details { get; set; } = string.Empty;
    }

    public class AiModelInfo
    {
        public string ModelName { get; }

        public AiModelInfo(string modelName)
        {
            ModelName = modelName;
        }

        public override string ToString() => ModelName;
    }

    public partial class AppSettingsPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly IDialogService _dialogService;
        private readonly ILlmProviderFactory _aiProviderFactory;
        private readonly UpdateService _updateService;
        private readonly PocketMC.Infrastructure.Diagnostics.DiagnosticReportingService _diagnosticService;
        private readonly PocketMC.Infrastructure.Diagnostics.DependencyHealthMonitor _healthMonitor;
        private readonly IDiscordRpcService _discordRpcService;
        private readonly WindowsStartupService _windowsStartupService;
        private readonly ServerSleepPreventionCoordinator _sleepPreventionCoordinator;
        private readonly AccentColorService _accentColorService;
        private readonly InstanceRegistry _registry;
        private readonly IServerLifecycleService _serverLifecycleService;
        private readonly PlayitAgentService _playitAgentService;
        private bool _isInitializing = true;
        private static readonly (string Name, string Hex)[] AccentColorPresets =
        {
            ("Blue", "#0078D4"),
            ("Dark Blue", "#003E92"),
            ("Teal", "#008272"),
            ("Cyan", "#0099BC"),
            ("Pocket MC Green", "#008B00"),
            ("Emerald", "#10893E"),
            ("Yellow", "#986F0B"),
            ("Orange", "#CA5010"),
            ("Red", "#D13438"),
            ("Rose", "#E3008C"),
            ("Purple", "#744DA9"),
            ("Violet", "#B146C2"),
            ("Slate", "#647687"),
            ("Steel", "#525E7D"),
            ("Gold", "#C19C00"),
            ("Coral", "#E74856")
        };

        public CloudBackupSettingsViewModel CloudBackups { get; }

        public AppSettingsPage(
            ApplicationState applicationState,
            SettingsManager settingsManager,
            IDialogService dialogService,
            ILlmProviderFactory aiProviderFactory,
            UpdateService updateService,
            PocketMC.Infrastructure.Diagnostics.DiagnosticReportingService diagnosticService,
            PocketMC.Infrastructure.Diagnostics.DependencyHealthMonitor healthMonitor,
            CloudBackupSettingsViewModel cloudBackups,
            WindowsStartupService windowsStartupService,
            IDiscordRpcService discordRpcService,
            ServerSleepPreventionCoordinator sleepPreventionCoordinator,
            AccentColorService accentColorService,
            InstanceRegistry registry,
            IServerLifecycleService serverLifecycleService,
            PlayitAgentService playitAgentService)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _dialogService = dialogService;
            _aiProviderFactory = aiProviderFactory;
            _updateService = updateService;
            _diagnosticService = diagnosticService;
            _healthMonitor = healthMonitor;
            _windowsStartupService = windowsStartupService;
            _discordRpcService = discordRpcService;
            _sleepPreventionCoordinator = sleepPreventionCoordinator;
            _accentColorService = accentColorService;
            _registry = registry;
            _serverLifecycleService = serverLifecycleService;
            _playitAgentService = playitAgentService;
            CloudBackups = cloudBackups;

            Loaded += AppSettingsPage_Loaded;
            Unloaded += AppSettingsPage_Unloaded;
        }

        private void AppSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.EnableMouseWheelScrolling(this, MainScrollViewer);
            ScrollViewerHelper.DisableAncestorScrollViewers(this);

            _isInitializing = true;
            RootDirectoryPathInput.Text = _applicationState.Settings.AppRootPath ?? "";
            CurseForgeKeyInput.Text = _applicationState.Settings.CurseForgeApiKey ?? "";

            // Setup Backdrop Combo
            BackdropCombo.Items.Clear();
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                BackdropCombo.Items.Add(new ComboBoxItem { Content = "Mica (Windows 11)", Tag = "Mica" });
                BackdropCombo.Items.Add(new ComboBoxItem { Content = "Acrylic", Tag = "Acrylic" });
            }
            BackdropCombo.Items.Add(new ComboBoxItem { Content = "Wallpaper Blur", Tag = "FakeMica" });
            BackdropCombo.Items.Add(new ComboBoxItem { Content = "Solid Dark", Tag = "Dark" });
            BackdropCombo.Items.Add(new ComboBoxItem { Content = "Solid Light", Tag = "Light" });

            string savedBackdrop = _applicationState.Settings.WindowBackdrop ?? "Acrylic";
            foreach (ComboBoxItem item in BackdropCombo.Items)
            {
                if (item.Tag?.ToString() == savedBackdrop)
                {
                    BackdropCombo.SelectedItem = item;
                    break;
                }
            }

            if (BackdropCombo.SelectedItem == null && BackdropCombo.Items.Count > 0)
            {
                BackdropCombo.SelectedIndex = 0;
            }

            // Initialize custom background panel state
            UpdateCustomBackgroundPanelVisibility();
            UpdateCustomBackgroundUI();
            InitializeAccentColorSection();

            // Set initial state
            ExternalBackupPathInput.Text = _applicationState.Settings.ExternalBackupDirectory ?? "";
            ToggleStartWithWindows.IsChecked = _applicationState.Settings.StartWithWindows;
            ToggleStartOnSystemBoot.IsChecked = _applicationState.Settings.StartOnSystemBoot;
            ToggleStartMinimizedToTray.IsChecked = _applicationState.Settings.StartMinimizedToTray;
            ToggleMinimizeToTrayOnClose.IsChecked = _applicationState.Settings.MinimizeToTrayOnClose;
            ToggleKeepComputerAwakeWhileServersRunning.IsChecked = _applicationState.Settings.KeepComputerAwakeWhileServersRunning;
            ToggleTelemetry.IsChecked = _applicationState.Settings.EnableTelemetry;

            // AI Settings
            AiApiKeyInput.Text = _applicationState.Settings.GetCurrentAiKey() ?? "";

            var initialProviderType = _aiProviderFactory.ParseProvider(_applicationState.Settings.AiProvider ?? "Gemini");
            var (initialDefaultModel, initialDefaultEndpoint) = _aiProviderFactory.GetProviderDefaults(initialProviderType);

            PopulateModelsForProvider(initialProviderType);
            SetSelectedModel(_applicationState.Settings.GetCurrentAiModel() ?? initialDefaultModel);

            AiEndpointUrlInput.Text = _applicationState.Settings.GetCurrentAiEndpoint() ?? initialDefaultEndpoint;
            EndpointUrlPanel.Visibility = initialProviderType == AiProviderType.Ollama ? Visibility.Visible : Visibility.Collapsed;

            ToggleAiSummarization.IsChecked = _applicationState.Settings.EnableAiSummarization;
            ToggleAutoSummarize.IsChecked = _applicationState.Settings.AlwaysAutoSummarize;

            // Set provider combo selection
            var savedProvider = _applicationState.Settings.AiProvider ?? "Gemini";
            var providerType = _aiProviderFactory.ParseProvider(savedProvider);
            var displayName = _aiProviderFactory.GetDisplayName(providerType);
            for (int i = 0; i < AiProviderCombo.Items.Count; i++)
            {
                if (AiProviderCombo.Items[i] is ComboBoxItem item && item.Content?.ToString() == displayName)
                {
                    AiProviderCombo.SelectedIndex = i;
                    break;
                }
            }

            _healthMonitor.HealthChanged += UpdateDependencyHealth;
            UpdateDependencyHealth();

            // Discord RPC
            ToggleDiscordRpc.IsChecked = _applicationState.Settings.EnableDiscordRpc;

            // Notifications
            ToggleServerOnlineNotifications.IsChecked = _applicationState.Settings.EnableServerOnlineNotifications;
            ToggleAgentConnectNotifications.IsChecked = _applicationState.Settings.EnableAgentConnectNotifications;
            ToggleRemoteControlNotifications.IsChecked = _applicationState.Settings.EnableRemoteControlNotifications;
            ToggleAiSummaryNotifications.IsChecked = _applicationState.Settings.EnableAiSummaryNotifications;

            _isInitializing = false;
        }

        private void AppSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.DisableMouseWheelScrolling(this);
            _healthMonitor.HealthChanged -= UpdateDependencyHealth;
        }

        // Remote Control UI logic has been moved to RemoteControlSettingsViewModel.

        private void ToggleStartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveStartupBehaviorSettings();
        }

        private void ToggleStartOnSystemBoot_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveStartupBehaviorSettings();
        }

        private void ToggleStartMinimizedToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveStartupBehaviorSettings();
        }

        private void ToggleMinimizeToTrayOnClose_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            bool previousMinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
            settings.MinimizeToTrayOnClose = ToggleMinimizeToTrayOnClose.IsChecked == true;

            try
            {
                _settingsManager.Save(settings);
            }
            catch (Exception ex)
            {
                settings.MinimizeToTrayOnClose = previousMinimizeToTrayOnClose;
                RevertAppBehaviorToggles(
                    settings.StartWithWindows,
                    settings.StartOnSystemBoot,
                    settings.StartMinimizedToTray,
                    previousMinimizeToTrayOnClose);
                _dialogService.ShowMessage(
                    "Settings Error",
                    $"Could not update app behavior settings:\n{ex.Message}",
                    DialogType.Error);
            }
        }

        private void ToggleTelemetry_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            settings.EnableTelemetry = ToggleTelemetry.IsChecked == true;

            try
            {
                _settingsManager.Save(settings);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(
                    "Settings Error",
                    $"Could not update telemetry settings:\n{ex.Message}",
                    DialogType.Error);
            }
        }

        private void ToggleKeepComputerAwakeWhileServersRunning_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            bool previousKeepAwake = settings.KeepComputerAwakeWhileServersRunning;
            settings.KeepComputerAwakeWhileServersRunning = ToggleKeepComputerAwakeWhileServersRunning.IsChecked == true;

            try
            {
                _settingsManager.Save(settings);
                _sleepPreventionCoordinator.Refresh();
            }
            catch (Exception ex)
            {
                settings.KeepComputerAwakeWhileServersRunning = previousKeepAwake;
                RevertPowerManagementToggle(previousKeepAwake);
                _sleepPreventionCoordinator.Refresh();
                _dialogService.ShowMessage(
                    "Settings Error",
                    $"Could not update power management settings:\n{ex.Message}",
                    DialogType.Error);
            }
        }

        private void SaveStartupBehaviorSettings()
        {
            var settings = _applicationState.Settings;
            bool previousStartWithWindows = settings.StartWithWindows;
            bool previousStartOnSystemBoot = settings.StartOnSystemBoot;
            bool previousStartMinimizedToTray = settings.StartMinimizedToTray;
            bool previousMinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;

            settings.StartWithWindows = ToggleStartWithWindows.IsChecked == true;
            settings.StartOnSystemBoot = ToggleStartOnSystemBoot.IsChecked == true;
            settings.StartMinimizedToTray = ToggleStartMinimizedToTray.IsChecked == true;
            settings.MinimizeToTrayOnClose = ToggleMinimizeToTrayOnClose.IsChecked == true;

            try
            {
                _windowsStartupService.Apply(settings);
                _windowsStartupService.ApplyBootTask(settings);
                _settingsManager.Save(settings);
            }
            catch (Exception ex)
            {
                settings.StartWithWindows = previousStartWithWindows;
                settings.StartOnSystemBoot = previousStartOnSystemBoot;
                settings.StartMinimizedToTray = previousStartMinimizedToTray;
                settings.MinimizeToTrayOnClose = previousMinimizeToTrayOnClose;

                try
                {
                    _windowsStartupService.Apply(settings);
                    _windowsStartupService.ApplyBootTask(settings);
                }
                catch
                {
                    // The visible toggle state still rolls back even if Windows rejects rollback too.
                }

                RevertAppBehaviorToggles(
                    previousStartWithWindows,
                    previousStartOnSystemBoot,
                    previousStartMinimizedToTray,
                    previousMinimizeToTrayOnClose);
                _dialogService.ShowMessage(
                    "Windows Startup",
                    $"Could not update Windows startup settings:\n{ex.Message}",
                    DialogType.Error);
            }
        }

        private void RevertAppBehaviorToggles(
            bool startWithWindows,
            bool startOnSystemBoot,
            bool startMinimizedToTray,
            bool minimizeToTrayOnClose)
        {
            bool wasInitializing = _isInitializing;
            _isInitializing = true;
            ToggleStartWithWindows.IsChecked = startWithWindows;
            ToggleStartOnSystemBoot.IsChecked = startOnSystemBoot;
            ToggleStartMinimizedToTray.IsChecked = startMinimizedToTray;
            ToggleMinimizeToTrayOnClose.IsChecked = minimizeToTrayOnClose;
            _isInitializing = wasInitializing;
        }

        private void RevertPowerManagementToggle(bool keepComputerAwakeWhileServersRunning)
        {
            bool wasInitializing = _isInitializing;
            _isInitializing = true;
            ToggleKeepComputerAwakeWhileServersRunning.IsChecked = keepComputerAwakeWhileServersRunning;
            _isInitializing = wasInitializing;
        }

        private void UpdateDependencyHealth()
        {
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => UpdateDependencyHealth());
                return;
            }

            var items = new System.Collections.Generic.List<DependencyHealthItem>();
            foreach (var health in _healthMonitor.GetAllHealth())
            {
                var brush = health.Status switch
                {
                    PocketMC.Domain.Models.DependencyHealthStatus.Healthy => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1)),
                    PocketMC.Domain.Models.DependencyHealthStatus.Degraded => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF)),
                    PocketMC.Domain.Models.DependencyHealthStatus.Down => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8)),
                    _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4))
                };

                string details = health.ErrorMessage ?? $"{health.Latency.TotalMilliseconds:F0}ms";
                if (health.Status == PocketMC.Domain.Models.DependencyHealthStatus.Unknown) details = "Pending check...";

                items.Add(new DependencyHealthItem
                {
                    Name = health.Name,
                    Status = health.Status.ToString(),
                    ColorBrush = brush,
                    Details = details
                });
            }
            DependencyHealthList.ItemsSource = items;
        }

        private void BackdropCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (BackdropCombo.SelectedItem is ComboBoxItem item && item.Tag is string backdropTag)
            {
                var settings = _applicationState.Settings;
                settings.WindowBackdrop = backdropTag;
                _settingsManager.Save(settings);

                UpdateCustomBackgroundPanelVisibility();

                if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
                {
                    mainWin.ApplyTheme(); // Apply global resources
                    mainWin.RequestMicaUpdate(); // Apply backdrop layer
                }
            }
        }

        private void InitializeAccentColorSection()
        {
            if (ColorSwatchPanel == null) return;

            bool wasInitializing = _isInitializing;
            _isInitializing = true;

            ColorSwatchPanel.Children.Clear();
            foreach (var preset in AccentColorPresets)
            {
                if (!AccentColorService.TryParseHexColor(preset.Hex, out Color presetColor, out string normalizedHex))
                {
                    continue;
                }

                var swatch = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = CreateAccentBrush(presetColor),
                    BorderThickness = new Thickness(1),
                    BorderBrush = CreateAccentBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                    Tag = normalizedHex,
                    ToolTip = $"{preset.Name} ({normalizedHex})"
                };
                swatch.MouseLeftButtonDown += ColorSwatch_Click;
                ColorSwatchPanel.Children.Add(swatch);
            }

            var settings = _applicationState.Settings;
            bool useCustom = AccentColorService.IsCustomMode(settings.AccentColorMode);
            AccentModeCombo.SelectedIndex = useCustom ? 1 : 0;

            string selectedHex = useCustom && !string.IsNullOrWhiteSpace(settings.CustomAccentColor)
                ? settings.CustomAccentColor
                : AccentColorService.DefaultCustomAccentColor;

            HexColorInput.Text = selectedHex;
            UpdateCustomAccentPanelVisibility();
            UpdateAccentPreview(_accentColorService.GetCurrentAccentColor());
            UpdateAccentSelection(useCustom ? selectedHex : null);

            _isInitializing = wasInitializing;
        }

        private void AccentModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            bool useCustom = AccentModeCombo.SelectedIndex == 1;

            if (useCustom)
            {
                string hex = GetCurrentCustomAccentHex();
                if (!AccentColorService.TryParseHexColor(hex, out Color color, out string normalizedHex))
                {
                    normalizedHex = AccentColorService.DefaultCustomAccentColor;
                    AccentColorService.TryParseHexColor(normalizedHex, out color, out _);
                }

                settings.AccentColorMode = AccentColorService.CustomMode;
                settings.CustomAccentColor = normalizedHex;
                HexColorInput.Text = normalizedHex;
                _settingsManager.Save(settings);

                _accentColorService.ApplyCustomAccent(color);
                if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ApplyTheme();
                }

                UpdateAccentPreview(color);
                UpdateAccentSelection(normalizedHex);

                _dialogService.ShowMessage(
                    "Accent Color Applied",
                    $"Successfully applied custom accent color {normalizedHex}.",
                    DialogType.Information);
            }
            else
            {
                settings.AccentColorMode = AccentColorService.AutomaticMode;
                _settingsManager.Save(settings);

                _accentColorService.ApplySystemAccent();
                if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ApplyTheme();
                }

                UpdateAccentPreview(_accentColorService.GetCurrentAccentColor());
                UpdateAccentSelection(null);

                _dialogService.ShowMessage(
                    "Accent Color Applied",
                    "Successfully applied Windows system accent color.",
                    DialogType.Information);
            }

            UpdateCustomAccentPanelVisibility();
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: string hex } &&
                AccentColorService.TryParseHexColor(hex, out Color color, out string normalizedHex))
            {
                SaveAndApplyCustomAccent(color, normalizedHex);
            }
        }

        private void ApplyHexColor_Click(object sender, RoutedEventArgs e)
        {
            ApplyHexColorFromInput();
        }

        private void HexColorInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            ApplyHexColorFromInput();
            e.Handled = true;
        }



        private void ApplyHexColorFromInput()
        {
            string input = HexColorInput.Text.Trim();
            if (!AccentColorService.TryParseHexColor(input, out Color color, out string normalizedHex))
            {
                _dialogService.ShowMessage(
                    "Invalid Accent Color",
                    "Enter a 6-digit hex color, for example #0078D4.",
                    DialogType.Warning);
                return;
            }

            SaveAndApplyCustomAccent(color, normalizedHex);
        }

        private void SaveAndApplyCustomAccent(Color color, string normalizedHex)
        {
            var settings = _applicationState.Settings;
            settings.AccentColorMode = AccentColorService.CustomMode;
            settings.CustomAccentColor = normalizedHex;
            _settingsManager.Save(settings);

            bool wasInitializing = _isInitializing;
            _isInitializing = true;
            AccentModeCombo.SelectedIndex = 1;
            HexColorInput.Text = normalizedHex;
            _isInitializing = wasInitializing;

            _accentColorService.ApplyCustomAccent(color);
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ApplyTheme();
            }

            UpdateCustomAccentPanelVisibility();
            UpdateAccentPreview(color);
            UpdateAccentSelection(normalizedHex);

            _dialogService.ShowMessage(
                "Accent Color Applied",
                $"Successfully applied custom accent color {normalizedHex}.",
                DialogType.Information);
        }

        private string GetCurrentCustomAccentHex()
        {
            if (AccentColorService.TryParseHexColor(
                    _applicationState.Settings.CustomAccentColor,
                    out _,
                    out string normalizedHex))
            {
                return normalizedHex;
            }

            return AccentColorService.DefaultCustomAccentColor;
        }

        private void UpdateCustomAccentPanelVisibility()
        {
            if (CustomAccentPanel == null) return;

            bool useCustom = AccentModeCombo.SelectedIndex == 1;
            CustomAccentPanel.Visibility = useCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateAccentPreview(Color color)
        {
            if (AccentPreviewSwatch != null)
            {
                AccentPreviewSwatch.Background = CreateAccentBrush(color);
            }
        }

        private void UpdateAccentSelection(string? selectedHex)
        {
            if (ColorSwatchPanel == null) return;

            foreach (UIElement child in ColorSwatchPanel.Children)
            {
                if (child is not Border swatch) continue;

                bool isSelected = selectedHex != null &&
                                  string.Equals(swatch.Tag as string, selectedHex, StringComparison.OrdinalIgnoreCase);
                swatch.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                swatch.BorderBrush = isSelected
                    ? CreateAccentBrush(Colors.White)
                    : CreateAccentBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
            }
        }

        private static SolidColorBrush CreateAccentBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ── Custom Background Image Handlers ────────────────────────────────

        private void BrowseCustomBackground_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Custom Background Image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif;*.tiff|All Files|*.*",
                CheckFileExists = true
            };

            // Pre-select current custom image directory if one is set
            string? currentPath = _applicationState.Settings.CustomBackgroundImagePath;
            if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(currentPath) ?? "";
            }

            if (openFileDialog.ShowDialog() == true)
            {
                var settings = _applicationState.Settings;
                settings.CustomBackgroundImagePath = openFileDialog.FileName;
                _settingsManager.Save(settings);

                UpdateCustomBackgroundUI();

                // Apply immediately
                if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
                {
                    mainWin.ApplyTheme();
                    mainWin.RequestMicaUpdate();
                }
            }
        }

        private void ClearCustomBackground_Click(object sender, RoutedEventArgs e)
        {
            var settings = _applicationState.Settings;
            settings.CustomBackgroundImagePath = "pack://application:,,,/Assets/default_wallpaper.png";
            _settingsManager.Save(settings);

            UpdateCustomBackgroundUI();

            if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
            {
                mainWin.ApplyTheme();
                mainWin.RequestMicaUpdate();
            }
        }

        private void UseDesktopBackground_Click(object sender, RoutedEventArgs e)
        {
            var settings = _applicationState.Settings;
            settings.CustomBackgroundImagePath = null;
            _settingsManager.Save(settings);

            UpdateCustomBackgroundUI();

            if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
            {
                mainWin.ApplyTheme();
                mainWin.RequestMicaUpdate();
            }
        }



        private void UpdateCustomBackgroundPanelVisibility()
        {
            if (CustomBackgroundPanel == null) return;

            bool isFakeMica = false;
            if (BackdropCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                isFakeMica = tag.Equals("FakeMica", StringComparison.OrdinalIgnoreCase);
            }

            CustomBackgroundPanel.Visibility = isFakeMica ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateCustomBackgroundUI()
        {
            if (CustomBgPathLabel == null || CustomBgPreviewImage == null) return;

            string? customPath = _applicationState.Settings.CustomBackgroundImagePath;
            bool isDesktop = string.IsNullOrWhiteSpace(customPath);
            bool isOfficial = customPath == "pack://application:,,,/Assets/default_wallpaper.png";
            bool isCustom = !isDesktop && !isOfficial && File.Exists(customPath);

            BtnClearCustomBg.Visibility = Visibility.Collapsed;
            BtnUseDesktopBg.Visibility = Visibility.Collapsed;

            if (isCustom)
            {
                CustomBgPathLabel.Text = Path.GetFileName(customPath);
                BtnClearCustomBg.Visibility = Visibility.Visible;
                BtnUseDesktopBg.Visibility = Visibility.Visible;
                CustomBgPlaceholderIcon.Visibility = Visibility.Collapsed;

                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(customPath!, UriKind.Absolute);
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 160;
                    bi.EndInit();
                    bi.Freeze();

                    CustomBgPreviewImage.Source = bi;
                    CustomBgPreviewImage.Visibility = Visibility.Visible;
                }
                catch
                {
                    CustomBgPreviewImage.Visibility = Visibility.Collapsed;
                    CustomBgPlaceholderIcon.Visibility = Visibility.Visible;
                }
            }
            else if (isOfficial)
            {
                CustomBgPathLabel.Text = "PocketMC Default Wallpaper";
                BtnUseDesktopBg.Visibility = Visibility.Visible;
                CustomBgPlaceholderIcon.Visibility = Visibility.Collapsed;
                
                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri("pack://application:,,,/Assets/default_wallpaper.png", UriKind.Absolute);
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 160;
                    bi.EndInit();
                    bi.Freeze();

                    CustomBgPreviewImage.Source = bi;
                    CustomBgPreviewImage.Visibility = Visibility.Visible;
                }
                catch
                {
                    CustomBgPreviewImage.Source = null;
                    CustomBgPreviewImage.Visibility = Visibility.Collapsed;
                    CustomBgPlaceholderIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                CustomBgPathLabel.Text = "Windows Desktop Wallpaper";
                BtnClearCustomBg.Visibility = Visibility.Visible;
                CustomBgPreviewImage.Source = null;
                CustomBgPreviewImage.Visibility = Visibility.Collapsed;
                CustomBgPlaceholderIcon.Visibility = Visibility.Visible;
            }
        }
        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            _applicationState.Settings.CurseForgeApiKey = CurseForgeKeyInput.Text.Trim();
            _settingsManager.Save(_applicationState.Settings);
            _dialogService.ShowMessage("Saved", "API Configuration saved successfully.");
        }

        // ── AI Summarization Handlers ──────────────────────────────────

        private void AiProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            // Suppress cascading saves while we update all UI fields atomically.
            // Without this, SetSelectedModel triggers AiModelCombo_SelectionChanged → SaveAiSettings()
            // before the endpoint URL text is updated, writing the OLD provider's endpoint under
            // the NEW provider's key (e.g. Gemini's Google URL saved as Mistral's endpoint).
            bool wasInitializing = _isInitializing;
            _isInitializing = true;

            var settings = _applicationState.Settings;
            var providerType = GetSelectedProvider();
            var providerStr = providerType.ToString();
            settings.AiProvider = providerStr;

            // Auto-fill the key for the newly selected provider
            settings.AiApiKeys.TryGetValue(providerStr, out var key);
            AiApiKeyInput.Text = key ?? string.Empty;

            settings.AiModels.TryGetValue(providerStr, out var model);
            settings.AiEndpoints.TryGetValue(providerStr, out var endpoint);

            PopulateModelsForProvider(providerType);

            var (defaultModel, defaultEndpoint) = _aiProviderFactory.GetProviderDefaults(providerType);
            // Update endpoint BEFORE model to prevent stale endpoint being saved by cascading handlers
            AiEndpointUrlInput.Text = !string.IsNullOrWhiteSpace(endpoint) ? endpoint : defaultEndpoint;
            SetSelectedModel(!string.IsNullOrWhiteSpace(model) ? model : defaultModel);

            EndpointUrlPanel.Visibility = providerType == AiProviderType.Ollama ? Visibility.Visible : Visibility.Collapsed;

            _isInitializing = wasInitializing;

            // Single atomic save with all fields correctly set
            SaveAiSettings();
        }

        private void AiModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            SaveAiSettings();
        }

        private void AiModelCombo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            SaveAiSettings();
        }

        private void PopulateModelsForProvider(AiProviderType provider)
        {
            if (AiModelCombo == null) return;

            AiModelCombo.Items.Clear();

            var models = GetModelsForProvider(provider);
            foreach (var m in models)
            {
                AiModelCombo.Items.Add(m);
            }
        }

        private System.Collections.Generic.List<AiModelInfo> GetModelsForProvider(AiProviderType provider)
        {
            var list = new System.Collections.Generic.List<AiModelInfo>();
            switch (provider)
            {
                case AiProviderType.Gemini:
                    list.Add(new AiModelInfo("gemini-2.5-flash"));
                    list.Add(new AiModelInfo("gemini-2.0-flash"));
                    list.Add(new AiModelInfo("gemini-1.5-flash"));
                    list.Add(new AiModelInfo("gemini-2.5-pro"));
                    list.Add(new AiModelInfo("gemini-2.0-pro-exp"));
                    list.Add(new AiModelInfo("gemini-1.5-pro"));
                    break;
                case AiProviderType.OpenAI:
                    list.Add(new AiModelInfo("gpt-4o-mini"));
                    list.Add(new AiModelInfo("gpt-4o"));
                    list.Add(new AiModelInfo("o1-mini"));
                    list.Add(new AiModelInfo("o3-mini"));
                    break;
                case AiProviderType.Claude:
                    list.Add(new AiModelInfo("claude-3-5-haiku-latest"));
                    list.Add(new AiModelInfo("claude-3-5-sonnet-latest"));
                    list.Add(new AiModelInfo("claude-3-opus-latest"));
                    break;
                case AiProviderType.Mistral:
                    list.Add(new AiModelInfo("open-mistral-7b"));
                    list.Add(new AiModelInfo("mistral-tiny"));
                    list.Add(new AiModelInfo("mistral-small-latest"));
                    list.Add(new AiModelInfo("mistral-medium-latest"));
                    list.Add(new AiModelInfo("mistral-large-latest"));
                    break;
                case AiProviderType.Groq:
                    list.Add(new AiModelInfo("llama-3.3-70b-versatile"));
                    list.Add(new AiModelInfo("llama3-8b-8192"));
                    list.Add(new AiModelInfo("mixtral-8x7b-32768"));
                    list.Add(new AiModelInfo("gemma2-9b-it"));
                    break;
                case AiProviderType.Ollama:
                    list.Add(new AiModelInfo("llama3"));
                    list.Add(new AiModelInfo("mistral"));
                    list.Add(new AiModelInfo("gemma2"));
                    list.Add(new AiModelInfo("phi3"));
                    break;
            }
            return list;
        }

        private void SetSelectedModel(string modelName)
        {
            if (AiModelCombo == null) return;

            foreach (var item in AiModelCombo.Items)
            {
                if (item is AiModelInfo info && string.Equals(info.ModelName, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    AiModelCombo.SelectedItem = info;
                    return;
                }
            }

            AiModelCombo.SelectedItem = null;
            AiModelCombo.Text = modelName;
        }

        private async void ValidateAiKey_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = AiApiKeyInput.Text.Trim();
            var modelName = AiModelCombo.Text.Trim();
            var endpointUrl = AiEndpointUrlInput.Text.Trim();
            var provider = GetSelectedProvider();

            if (provider != AiProviderType.Ollama && string.IsNullOrWhiteSpace(apiKey))
            {
                AiKeyStatus.Text = "⚠ Please enter an API key first.";
                AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                return;
            }

            string maskedKey = apiKey.Length > 8
                ? apiKey[..4] + "..." + apiKey[^4..]
                : "invalid/too-short";

            AiKeyStatus.Text = $"⏳ Validating {_aiProviderFactory.GetDisplayName(provider)} with key {maskedKey}...";
            AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));

            try
            {
                var result = await _aiProviderFactory.GetProvider(provider).ValidateKeyAsync(apiKey, modelName, endpointUrl);
                if (result.Success)
                {
                    AiKeyStatus.Text = "✅ API key is valid! Connection successful.";
                    AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                }
                else
                {
                    AiKeyStatus.Text = $"❌ {result.Error}";
                    AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                }
            }
            catch (Exception ex)
            {
                AiKeyStatus.Text = $"❌ Error: {ex.Message}";
                AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
            }
        }

        private void SaveAiKey_Click(object sender, RoutedEventArgs e)
        {
            SaveAiSettings();
            _dialogService.ShowMessage("Saved", "AI Summarization configuration saved successfully.");
        }

        private void ToggleAiSummarization_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveAiSettings();
        }

        private void ToggleAutoSummarize_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SaveAiSettings();
        }

        private void SaveAiSettings()
        {
            var settings = _applicationState.Settings;
            var provider = GetSelectedProvider().ToString();

            settings.AiProvider = provider;
            settings.AiApiKeys[provider] = AiApiKeyInput.Text.Trim();
            settings.AiModels[provider] = AiModelCombo.Text.Trim();
            settings.AiEndpoints[provider] = AiEndpointUrlInput.Text.Trim();

            settings.EnableAiSummarization = ToggleAiSummarization.IsChecked == true;
            settings.AlwaysAutoSummarize = ToggleAutoSummarize.IsChecked == true;
            _settingsManager.Save(settings);
        }

        private AiProviderType GetSelectedProvider()
        {
            if (AiProviderCombo.SelectedItem is ComboBoxItem item)
            {
                string? name = item.Content?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return _aiProviderFactory.ParseProvider(name);
            }
            return AiProviderType.Gemini;
        }

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            // Handled in AboutPage now
        }

        // ── Discord RPC Toggle ────────────────────────────────────────────

        private void ToggleDiscordRpc_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            settings.EnableDiscordRpc = ToggleDiscordRpc.IsChecked == true;
            _settingsManager.Save(settings);

            if (settings.EnableDiscordRpc)
                _discordRpcService.Initialize();
            else
                _discordRpcService.Shutdown();
        }

        private void ToggleServerOnlineNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            settings.EnableServerOnlineNotifications = ToggleServerOnlineNotifications.IsChecked == true;
            _settingsManager.Save(settings);
        }

        private void ToggleAgentConnectNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            settings.EnableAgentConnectNotifications = ToggleAgentConnectNotifications.IsChecked == true;
            _settingsManager.Save(settings);
        }

        private void ToggleRemoteControlNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            settings.EnableRemoteControlNotifications = ToggleRemoteControlNotifications.IsChecked == true;
            _settingsManager.Save(settings);
        }

        private void ToggleAiSummaryNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = _applicationState.Settings;
            settings.EnableAiSummaryNotifications = ToggleAiSummaryNotifications.IsChecked == true;
            _settingsManager.Save(settings);
        }

        private void CopyDiscordInvite_Click(object sender, RoutedEventArgs e)
        {
            // Handled in AboutPage now
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            UpdateStatusText.Visibility = Visibility.Visible;
            UpdateStatusText.Text = "⏳ Checking for updates...";
            UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));

            try
            {
                await _updateService.CheckAndDownloadAsync();

                if (_updateService.HasPendingUpdate)
                {
                    UpdateStatusText.Text = $"✅ Update ready: {_updateService.PendingVersion}. See the top banner to restart.";
                    UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                }
                else
                {
                    UpdateStatusText.Text = "✅ PocketMC is up to date!";
                    UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"❌ Error: {ex.Message}";
                UpdateStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private void BrowseExternalBackup_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select External Backup Folder (e.g. Google Drive/Dropbox sync folder)"
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ExternalBackupPathInput.Text = folderDialog.SelectedPath;
            }
        }

        private void SaveExternalBackup_Click(object sender, RoutedEventArgs e)
        {
            var path = ExternalBackupPathInput.Text.Trim();

            if (!string.IsNullOrWhiteSpace(path) && !System.IO.Directory.Exists(path))
            {
                _dialogService.ShowMessage("Invalid Path", "The selected directory does not exist or is inaccessible.");
                return;
            }

            var settings = _applicationState.Settings;
            settings.ExternalBackupDirectory = string.IsNullOrWhiteSpace(path) ? null : path;
            _settingsManager.Save(settings);

            _dialogService.ShowMessage("Saved", "External backup location saved. Next time a server backup runs, it will be automatically replicated here.");
        }

        private async void ExportBundle_Click(object sender, RoutedEventArgs e)
        {
            BtnExportBundle.IsEnabled = false;
            ExportBundleStatusText.Visibility = Visibility.Visible;
            ExportBundleStatusText.Text = "⏳ Generating support bundle (This may take a moment)...";
            ExportBundleStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));

            try
            {
                // Put it on Desktop by default
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string bundlePath = await _diagnosticService.GenerateSupportBundleAsync(desktopPath);

                ExportBundleStatusText.Text = $"✅ Bundle saved to Desktop: {System.IO.Path.GetFileName(bundlePath)}";
                ExportBundleStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));

                // Select file in explorer
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{bundlePath}\"");
            }
            catch (Exception ex)
            {
                ExportBundleStatusText.Text = $"❌ Failed to generate bundle: {ex.Message}";
                ExportBundleStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x38, 0xA8));
            }
            finally
            {
                BtnExportBundle.IsEnabled = true;
            }
        }

        // ── UWP Loopback Fix ─────────────────────────────────────────────────
        // "Fix Bedrock LAN / Localhost" button — wires to UwpLoopbackHelper.
        // The button and its status TextBlock should be named BtnFixUwpLoopback
        // and UwpLoopbackStatusText in the XAML (add them to the Network section).

        private async void BtnFixUwpLoopback_Click(object sender, RoutedEventArgs e)
        {
            // Guard: check if element exists (XAML may not have it yet in older builds).
            if (FindName("BtnFixUwpLoopback") is not System.Windows.Controls.Button btn) return;
            System.Windows.Controls.TextBlock? statusText = FindName("UwpLoopbackStatusText") as System.Windows.Controls.TextBlock;

            btn.IsEnabled = false;

            try
            {
                // Fast-path: already exempt — no need for another UAC prompt.
                if (UwpLoopbackHelper.IsExemptionPresent())
                {
                    if (statusText != null)
                    {
                        statusText.Text = "✅ Loopback exemption is already active. Bedrock can connect to localhost.";
                        statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                        statusText.Visibility = Visibility.Visible;
                    }
                    return;
                }

                if (statusText != null)
                {
                    statusText.Text = "⏳ Requesting elevation — please approve the UAC prompt...";
                    statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));
                    statusText.Visibility = Visibility.Visible;
                }

                bool success = await UwpLoopbackHelper.ApplyExemptionAsync();

                if (statusText != null)
                {
                    if (success)
                    {
                        statusText.Text = "✅ Loopback exemption applied! Restart Minecraft Bedrock and try connecting to localhost.";
                        statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                    }
                    else
                    {
                        statusText.Text = "⚠ Could not apply the exemption. You may have cancelled the UAC prompt, or CheckNetIsolation.exe is unavailable.";
                        statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF));
                    }
                    statusText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                if (statusText != null)
                {
                    statusText.Text = $"❌ Error: {ex.Message}";
                    statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                    statusText.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch
            {
                // Ignore
            }
        }

        private async void ChangeRootDirectory_Click(object sender, RoutedEventArgs e)
        {
            BtnChangeRootDirectory.IsEnabled = false;
            try
            {
                string? selectedPath = await _dialogService.OpenFolderDialogAsync("Select a new PocketMC Root Directory");
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                string currentRoot = _applicationState.Settings.AppRootPath ?? "";
                string targetPath = RootDirectorySetupHelper.ResolveRootPath(selectedPath);

                if (string.Equals(currentRoot, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    _dialogService.ShowMessage("Invalid Directory", "The selected directory is the same as the current root folder.", DialogType.Warning);
                    return;
                }

                // Check bi-directional nesting/containment
                if (!string.IsNullOrEmpty(currentRoot) && !string.IsNullOrEmpty(targetPath))
                {
                    string normCurrent = Path.GetFullPath(currentRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string normTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    if (normTarget.StartsWith(normCurrent, StringComparison.OrdinalIgnoreCase) ||
                        normCurrent.StartsWith(normTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        _dialogService.ShowMessage("Invalid Directory", "The selected directory cannot be nested within or overlap with the current root directory.", DialogType.Error);
                        return;
                    }
                }

                // Check if any server is running
                var instances = _registry.GetAll();
                bool anyRunning = instances.Any(i => _serverLifecycleService.IsRunning(i.Id));
                if (anyRunning)
                {
                    _dialogService.ShowMessage("Active Servers Running", "All running servers must be stopped before changing the root directory.", DialogType.Error);
                    return;
                }

                // Show confirmation dialog with Transfer vs Update options
                var confirmResult = await _dialogService.ShowDialogAsync(
                    "Change Location & Transfer Data",
                    "Do you want to transfer (move) all your existing servers, runtimes, and playit binaries to the new location?\n\n" +
                    "• Transfer Data: Moves everything to the new folder. (Recommended)\n" +
                    "• Update Reference Only: Only updates the path settings, leaving your files in the old folder.",
                    DialogType.Question,
                    showCancel: true,
                    primaryButtonText: "Transfer Data",
                    secondaryButtonText: "Update Reference Only",
                    cancelButtonText: "Cancel");

                if (confirmResult == DialogResult.Cancel || confirmResult == DialogResult.Dismiss)
                {
                    return;
                }

                bool playitWasRunning = _playitAgentService.IsRunning;
                if (playitWasRunning)
                {
                    await _playitAgentService.StopAsync();
                }

                try
                {
                    if (confirmResult == DialogResult.Yes) // Transfer Data
                    {
                        // Copy files to target path
                        try
                        {
                            if (Directory.Exists(currentRoot))
                            {
                                await _dialogService.ShowProgressDialogAsync(
                                    "Transferring Data",
                                    "Transferring files to the new root directory... Do not close PocketMC.",
                                    async (progress) =>
                                    {
                                        await PocketMC.Domain.Storage.FileUtils.CopyDirectoryAsync(currentRoot, targetPath, progress);
                                    });
                            }
                        }
                        catch (Exception ex)
                        {
                            _dialogService.ShowMessage("Transfer Failed", $"Failed to copy data to the new folder:\n\n{ex.Message}", DialogType.Error);
                            return;
                        }

                        // Update Settings
                        var settings = _settingsManager.Load();
                        settings.AppRootPath = targetPath;
                        _settingsManager.Save(settings);
                        _applicationState.ApplySettings(settings);
                        _registry.Refresh();

                        // Try delete old directory
                        try
                        {
                            if (Directory.Exists(currentRoot))
                            {
                                await PocketMC.Domain.Storage.FileUtils.CleanDirectoryAsync(currentRoot);
                            }
                        }
                        catch (Exception ex)
                        {
                            _dialogService.ShowMessage("Cleanup Warning", $"The data was transferred successfully and settings updated, but some files in the old folder could not be deleted:\n\n{ex.Message}\n\nYou can safely delete the old folder manually.", DialogType.Warning);
                            RootDirectoryPathInput.Text = targetPath;
                            return;
                        }

                        _dialogService.ShowMessage("Success", "The data has been successfully transferred and the root directory updated.", DialogType.Information);
                    }
                    else if (confirmResult == DialogResult.No) // Update Reference Only
                    {
                        var settings = _settingsManager.Load();
                        settings.AppRootPath = targetPath;
                        _settingsManager.Save(settings);
                        _applicationState.ApplySettings(settings);
                        _registry.Refresh();

                        _dialogService.ShowMessage("Success", "Root directory reference updated. No files were moved.", DialogType.Information);
                    }

                    RootDirectoryPathInput.Text = targetPath;
                }
                finally
                {
                    if (playitWasRunning)
                    {
                        _playitAgentService.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", $"An unexpected error occurred: {ex.Message}", DialogType.Error);
            }
            finally
            {
                BtnChangeRootDirectory.IsEnabled = true;
            }
        }
    }
}
