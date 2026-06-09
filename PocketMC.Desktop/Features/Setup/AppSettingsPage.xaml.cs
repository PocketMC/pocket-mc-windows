using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Infrastructure.Power;

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
        private readonly AiApiClient _aiApiClient;
        private readonly UpdateService _updateService;
        private readonly PocketMC.Desktop.Features.Diagnostics.DiagnosticReportingService _diagnosticService;
        private readonly PocketMC.Desktop.Features.Diagnostics.DependencyHealthMonitor _healthMonitor;
        private readonly IDiscordRpcService _discordRpcService;
        private readonly WindowsStartupService _windowsStartupService;
        private readonly ServerSleepPreventionCoordinator _sleepPreventionCoordinator;
        private bool _isInitializing = true;
        private readonly MouseWheelEventHandler _previewMouseWheelHandler;
        private bool _isForwardingMouseWheel;
        
        public CloudBackupSettingsViewModel CloudBackups { get; }

        public AppSettingsPage(
            ApplicationState applicationState, 
            SettingsManager settingsManager, 
            IDialogService dialogService, 
            AiApiClient aiApiClient,
            UpdateService updateService,
            PocketMC.Desktop.Features.Diagnostics.DiagnosticReportingService diagnosticService,
            PocketMC.Desktop.Features.Diagnostics.DependencyHealthMonitor healthMonitor,
            CloudBackupSettingsViewModel cloudBackups,
            WindowsStartupService windowsStartupService,
            IDiscordRpcService discordRpcService,
            ServerSleepPreventionCoordinator sleepPreventionCoordinator)
        {
            InitializeComponent();
            _previewMouseWheelHandler = OnPagePreviewMouseWheel;
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _dialogService = dialogService;
            _aiApiClient = aiApiClient;
            _updateService = updateService;
            _diagnosticService = diagnosticService;
            _healthMonitor = healthMonitor;
            _windowsStartupService = windowsStartupService;
            _discordRpcService = discordRpcService;
            _sleepPreventionCoordinator = sleepPreventionCoordinator;
            CloudBackups = cloudBackups;

            Loaded += AppSettingsPage_Loaded;
            Unloaded += AppSettingsPage_Unloaded;
        }

        private void AppSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Register mouse wheel handler with handledEventsToo=true
            // This catches wheel events even when internal WPF-UI controls consume them
            AddHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler, true);
            
            // CRITICAL: Disable the NavigationView's internal ScrollViewer so our page gets a finite height
            DisableParentScrollViewer(this);

            _isInitializing = true;
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

            // Set initial state
            ExternalBackupPathInput.Text = _applicationState.Settings.ExternalBackupDirectory ?? "";
            ToggleStartWithWindows.IsChecked = _applicationState.Settings.StartWithWindows;
            ToggleStartMinimizedToTray.IsChecked = _applicationState.Settings.StartMinimizedToTray;
            ToggleMinimizeToTrayOnClose.IsChecked = _applicationState.Settings.MinimizeToTrayOnClose;
            ToggleKeepComputerAwakeWhileServersRunning.IsChecked = _applicationState.Settings.KeepComputerAwakeWhileServersRunning;

            // AI Settings
            AiApiKeyInput.Text = _applicationState.Settings.GetCurrentAiKey() ?? "";
            
            var initialProviderType = AiApiClient.ParseProvider(_applicationState.Settings.AiProvider ?? "Gemini");
            var (initialDefaultModel, initialDefaultEndpoint) = AiApiClient.GetProviderDefaults(initialProviderType);
            
            PopulateModelsForProvider(initialProviderType);
            SetSelectedModel(_applicationState.Settings.GetCurrentAiModel() ?? initialDefaultModel);

            AiEndpointUrlInput.Text = _applicationState.Settings.GetCurrentAiEndpoint() ?? initialDefaultEndpoint;
            EndpointUrlPanel.Visibility = initialProviderType == AiProviderType.Ollama ? Visibility.Visible : Visibility.Collapsed;

            ToggleAiSummarization.IsChecked = _applicationState.Settings.EnableAiSummarization;
            ToggleAutoSummarize.IsChecked = _applicationState.Settings.AlwaysAutoSummarize;

            // Set provider combo selection
            var savedProvider = _applicationState.Settings.AiProvider ?? "Gemini";
            var providerType = AiApiClient.ParseProvider(savedProvider);
            var displayName = AiApiClient.GetDisplayName(providerType);
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
            RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);
            _healthMonitor.HealthChanged -= UpdateDependencyHealth;
        }

        // Remote Control UI logic has been moved to RemoteControlSettingsViewModel.

        private void ToggleStartWithWindows_Changed(object sender, RoutedEventArgs e)
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
                    settings.StartMinimizedToTray,
                    previousMinimizeToTrayOnClose);
                _dialogService.ShowMessage(
                    "Settings Error",
                    $"Could not update app behavior settings:\n{ex.Message}",
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
            bool previousStartMinimizedToTray = settings.StartMinimizedToTray;
            bool previousMinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;

            settings.StartWithWindows = ToggleStartWithWindows.IsChecked == true;
            settings.StartMinimizedToTray = ToggleStartMinimizedToTray.IsChecked == true;
            settings.MinimizeToTrayOnClose = ToggleMinimizeToTrayOnClose.IsChecked == true;

            try
            {
                _windowsStartupService.Apply(settings);
                _settingsManager.Save(settings);
            }
            catch (Exception ex)
            {
                settings.StartWithWindows = previousStartWithWindows;
                settings.StartMinimizedToTray = previousStartMinimizedToTray;
                settings.MinimizeToTrayOnClose = previousMinimizeToTrayOnClose;

                try
                {
                    _windowsStartupService.Apply(settings);
                }
                catch
                {
                    // The visible toggle state still rolls back even if Windows rejects rollback too.
                }

                RevertAppBehaviorToggles(
                    previousStartWithWindows,
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
            bool startMinimizedToTray,
            bool minimizeToTrayOnClose)
        {
            bool wasInitializing = _isInitializing;
            _isInitializing = true;
            ToggleStartWithWindows.IsChecked = startWithWindows;
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
            if (Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                Application.Current.Dispatcher.BeginInvoke(() => UpdateDependencyHealth());
                return;
            }

            var items = new System.Collections.Generic.List<DependencyHealthItem>();
            foreach (var health in _healthMonitor.GetAllHealth())
            {
                var brush = health.Status switch
                {
                    PocketMC.Desktop.Features.Diagnostics.DependencyHealthStatus.Healthy => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1)),
                    PocketMC.Desktop.Features.Diagnostics.DependencyHealthStatus.Degraded => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF)),
                    PocketMC.Desktop.Features.Diagnostics.DependencyHealthStatus.Down => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8)),
                    _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4))
                };

                string details = health.ErrorMessage ?? $"{health.Latency.TotalMilliseconds:F0}ms";
                if (health.Status == PocketMC.Desktop.Features.Diagnostics.DependencyHealthStatus.Unknown) details = "Pending check...";

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
                    mainWin.RequestMicaUpdate(); // This will apply the backdrop
                }
            }
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
                    mainWin.RequestMicaUpdate();
                }
            }
        }

        private void ClearCustomBackground_Click(object sender, RoutedEventArgs e)
        {
            var settings = _applicationState.Settings;
            settings.CustomBackgroundImagePath = null;
            _settingsManager.Save(settings);

            UpdateCustomBackgroundUI();

            // Revert to wallpaper immediately
            if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
            {
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
            bool hasCustomImage = !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath);

            if (hasCustomImage)
            {
                CustomBgPathLabel.Text = Path.GetFileName(customPath);
                BtnClearCustomBg.Visibility = Visibility.Visible;
                CustomBgPlaceholderIcon.Visibility = Visibility.Collapsed;

                // Load thumbnail preview
                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(customPath!, UriKind.Absolute);
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 160; // Small thumbnail
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
            else
            {
                CustomBgPathLabel.Text = "No custom image selected";
                BtnClearCustomBg.Visibility = Visibility.Collapsed;
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

            var (defaultModel, defaultEndpoint) = AiApiClient.GetProviderDefaults(providerType);
            SetSelectedModel(!string.IsNullOrWhiteSpace(model) ? model : defaultModel);
            AiEndpointUrlInput.Text = !string.IsNullOrWhiteSpace(endpoint) ? endpoint : defaultEndpoint;
            
            EndpointUrlPanel.Visibility = providerType == AiProviderType.Ollama ? Visibility.Visible : Visibility.Collapsed;

            _settingsManager.Save(settings);
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

            AiKeyStatus.Text = $"⏳ Validating {AiApiClient.GetDisplayName(provider)} with key {maskedKey}...";
            AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));

            try
            {
                var result = await _aiApiClient.ValidateKeyAsync(provider, apiKey, modelName, endpointUrl);
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
                    return AiApiClient.ParseProvider(name);
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
                if (PocketMC.Desktop.Infrastructure.UwpLoopbackHelper.IsExemptionPresent())
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

                bool success = await PocketMC.Desktop.Infrastructure.UwpLoopbackHelper.ApplyExemptionAsync();

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

        private void DisableParentScrollViewer(DependencyObject obj)
        {
            var parent = VisualTreeHelper.GetParent(obj);
            while (parent != null)
            {
                if (parent is ScrollViewer sv)
                {
                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        // ── Aggressive Mouse Wheel Scrolling ──────────────────────────────
        // Follows the proven pattern from ServerSettingsPage:
        // page-level AddHandler with handledEventsToo=true intercepts wheel
        // events regardless of which child control consumed them.

        private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_isForwardingMouseWheel || e.OriginalSource is not DependencyObject source)
                return;

            // 1. Never intercept if a ScrollBar thumb is being dragged
            if (FindAncestor<ScrollBar>(source) != null)
                return;

            // 2. Skip if inside an OPEN ComboBox dropdown (let it scroll its own list)
            var comboBox = FindAncestor<ComboBox>(source);
            if (comboBox?.IsDropDownOpen == true)
                return;

            // 3. Skip if inside a Popup (ComboBox dropdown popup, tooltip, etc.)
            if (FindAncestor<Popup>(source) != null)
                return;

            // 4. Forward the scroll to MainScrollViewer
            if (MainScrollViewer == null || MainScrollViewer.ScrollableHeight <= 0)
                return;

            e.Handled = true;

            try
            {
                _isForwardingMouseWheel = true;
                // Scroll by 3 lines per notch for responsive feel (matches ServerSettingsPage)
                int steps = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine) * 3;
                for (int i = 0; i < steps; i++)
                {
                    if (e.Delta > 0)
                        MainScrollViewer.LineUp();
                    else
                        MainScrollViewer.LineDown();
                }
            }
            finally
            {
                _isForwardingMouseWheel = false;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                DependencyObject? visualParent = null;
                try { visualParent = VisualTreeHelper.GetParent(current); } catch { }
                current = visualParent ?? LogicalTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
