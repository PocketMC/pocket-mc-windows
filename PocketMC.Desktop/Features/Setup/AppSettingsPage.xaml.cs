using System;
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
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Intelligence;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.Setup
{
    public class DependencyHealthItem
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public System.Windows.Media.Brush ColorBrush { get; set; } = System.Windows.Media.Brushes.White;
        public string Details { get; set; } = string.Empty;
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
        private bool _isInitializing = true;
        private readonly MouseWheelEventHandler _previewMouseWheelHandler;
        private bool _isForwardingMouseWheel;

        public AppSettingsPage(
            ApplicationState applicationState, 
            SettingsManager settingsManager, 
            IDialogService dialogService, 
            AiApiClient aiApiClient,
            UpdateService updateService,
            PocketMC.Desktop.Features.Diagnostics.DiagnosticReportingService diagnosticService,
            PocketMC.Desktop.Features.Diagnostics.DependencyHealthMonitor healthMonitor)
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
            ToggleMica.IsChecked = _applicationState.Settings.EnableMicaEffect;
            CurseForgeKeyInput.Text = _applicationState.Settings.CurseForgeApiKey ?? "";

            // Theme setting
            string savedTheme = _applicationState.Settings.ApplicationTheme ?? "System";
            foreach (ComboBoxItem item in ThemeCombo.Items)
            {
                if (item.Tag?.ToString() == savedTheme)
                {
                    ThemeCombo.SelectedItem = item;
                    break;
                }
            }

            // Set initial state
            ExternalBackupPathInput.Text = _applicationState.Settings.ExternalBackupDirectory ?? "";

            // AI Settings
            AiApiKeyInput.Text = _applicationState.Settings.GetCurrentAiKey() ?? "";
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

            _isInitializing = false;
        }

        private void AppSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);
            _healthMonitor.HealthChanged -= UpdateDependencyHealth;
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

        private void ToggleMica_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateMicaEffect(true);
        }

        private void ToggleMica_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateMicaEffect(false);
        }

        private void UpdateMicaEffect(bool enable)
        {
            var settings = _applicationState.Settings;
            settings.EnableMicaEffect = enable;
            _settingsManager.Save(settings);

            if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
            {
                mainWin.RequestMicaUpdate();
            }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string themeTag)
            {
                var settings = _applicationState.Settings;
                settings.ApplicationTheme = themeTag;
                _settingsManager.Save(settings);

                if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
                {
                    mainWin.RequestThemeUpdate(themeTag);
                }
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
            var providerStr = GetSelectedProvider().ToString();
            settings.AiProvider = providerStr;

            // Auto-fill the key for the newly selected provider
            settings.AiApiKeys.TryGetValue(providerStr, out var key);
            AiApiKeyInput.Text = key ?? string.Empty;

            _settingsManager.Save(settings);
        }

        private async void ValidateAiKey_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = AiApiKeyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AiKeyStatus.Text = "⚠ Please enter an API key first.";
                AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                return;
            }

            var provider = GetSelectedProvider();
            AiKeyStatus.Text = $"⏳ Validating with {AiApiClient.GetDisplayName(provider)}...";
            AiKeyStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA));

            try
            {
                var result = await _aiApiClient.ValidateKeyAsync(provider, apiKey);
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

            settings.EnableAiSummarization = ToggleAiSummarization.IsChecked == true;
            settings.AlwaysAutoSummarize = ToggleAutoSummarize.IsChecked == true;
            _settingsManager.Save(settings);
        }

        private AiProviderType GetSelectedProvider()
        {
            if (AiProviderCombo.SelectedItem is ComboBoxItem item && item.Content is string name)
                return AiApiClient.ParseProvider(name);
            return AiProviderType.Gemini;
        }

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            // Handled in AboutPage now
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
