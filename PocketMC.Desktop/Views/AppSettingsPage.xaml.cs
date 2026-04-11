using System;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Views
{
    public partial class AppSettingsPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly IDialogService _dialogService;
        private bool _isInitializing = true;

        public AppSettingsPage(ApplicationState applicationState, SettingsManager settingsManager, IDialogService dialogService)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _dialogService = dialogService;

            Loaded += AppSettingsPage_Loaded;
        }

        private void AppSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            ToggleMica.IsChecked = _applicationState.Settings.EnableMicaEffect;
            CurseForgeKeyInput.Text = _applicationState.Settings.CurseForgeApiKey ?? "";
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
    }
}
