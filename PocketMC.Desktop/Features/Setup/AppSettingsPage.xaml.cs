using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
    public partial class AppSettingsPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly IDialogService _dialogService;
        private readonly AiApiClient _aiApiClient;
        private readonly UpdateService _updateService;
        private readonly PocketMC.Desktop.Features.Diagnostics.DiagnosticReportingService _diagnosticService;
        private bool _isInitializing = true;

        public AppSettingsPage(
            ApplicationState applicationState, 
            SettingsManager settingsManager, 
            IDialogService dialogService, 
            AiApiClient aiApiClient,
            UpdateService updateService,
            PocketMC.Desktop.Features.Diagnostics.DiagnosticReportingService diagnosticService)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _dialogService = dialogService;
            _aiApiClient = aiApiClient;
            _updateService = updateService;
            _diagnosticService = diagnosticService;

            Loaded += AppSettingsPage_Loaded;
        }

        private void AppSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            ToggleMica.IsChecked = _applicationState.Settings.EnableMicaEffect;
            CurseForgeKeyInput.Text = _applicationState.Settings.CurseForgeApiKey ?? "";

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

            _isInitializing = false;
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
                ExportBundleStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
            }
            finally
            {
                BtnExportBundle.IsEnabled = true;
            }
        }
    }
}
