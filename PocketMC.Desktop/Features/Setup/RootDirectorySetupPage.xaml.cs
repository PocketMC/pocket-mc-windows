using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Infrastructure.Security;
using PocketMC.Infrastructure.Backups;
using PocketMC.Application.Services.Setup;
using PocketMC.Infrastructure.Java;
using PocketMC.Desktop.Features.Console;
using PocketMC.Infrastructure.Networking;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Domain.Models;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Core.Presentation;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.Setup
{
    public partial class RootDirectorySetupPage : Page
    {
        public event EventHandler<string>? DirectorySelected;
        private string? _selectedRootPath;
        private readonly SettingsManager _settingsManager;
        private readonly SettingsBackupService _settingsBackupService;
        private readonly ApplicationState _applicationState;
        private readonly IDialogService _dialogService;

        public RootDirectorySetupPage(
            SettingsManager settingsManager,
            SettingsBackupService settingsBackupService,
            ApplicationState applicationState,
            IDialogService dialogService)
        {
            InitializeComponent();
            _settingsManager = settingsManager;
            _settingsBackupService = settingsBackupService;
            _applicationState = applicationState;
            _dialogService = dialogService;

            string? defaultParentDirectory = RootDirectorySetupHelper.GetDefaultParentDirectory();
            if (defaultParentDirectory != null)
            {
                _selectedRootPath = Path.Combine(defaultParentDirectory, RootDirectorySetupHelper.SuggestedFolderName);
                TxtSuggestedPath.Text = _selectedRootPath;
                ContinueButton.IsEnabled = true;
            }
            else
            {
                _selectedRootPath = null;
                TxtSuggestedPath.Text = "Please click 'Select Directory' to select a home folder.";
                ContinueButton.IsEnabled = false;
            }
            TxtSuggestedFolderName.Text = RootDirectorySetupHelper.SuggestedFolderName;
        }

        public RootDirectorySetupPage() : this(
            ((App)System.Windows.Application.Current).Services.GetRequiredService<SettingsManager>(),
            ((App)System.Windows.Application.Current).Services.GetRequiredService<SettingsBackupService>(),
            ((App)System.Windows.Application.Current).Services.GetRequiredService<ApplicationState>(),
            ((App)System.Windows.Application.Current).Services.GetRequiredService<IDialogService>())
        {
        }

        private void BtnSelectDirectory_Click(object sender, RoutedEventArgs e)
        {
            string? defaultParentDirectory = RootDirectorySetupHelper.GetDefaultParentDirectory();
            string? suggestedFullPath = defaultParentDirectory != null
                ? Path.Combine(defaultParentDirectory, RootDirectorySetupHelper.SuggestedFolderName)
                : null;

            if (suggestedFullPath != null && !Directory.Exists(suggestedFullPath))
            {
                try
                {
                    Directory.CreateDirectory(suggestedFullPath);
                }
                catch
                {
                    // Ignore exception cleanly, if it fails here the dialog will still try to open
                }
            }

            var dialog = new OpenFolderDialog
            {
                Title = "Choose where to create the PocketMC root folder",
                Multiselect = false,
                InitialDirectory = suggestedFullPath,
                DefaultDirectory = defaultParentDirectory,
                FolderName = RootDirectorySetupHelper.SuggestedFolderName
            };

            if (dialog.ShowDialog() != true)
            {
                SelectDirectoryButton.Focus();
                return;
            }

            _selectedRootPath = RootDirectorySetupHelper.ResolveRootPath(dialog.FolderName);
            TxtSuggestedPath.Text = _selectedRootPath;
            ContinueButton.IsEnabled = true;
            ContinueButton.Focus();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedRootPath))
            {
                return;
            }

            if (!Directory.Exists(_selectedRootPath))
            {
                try
                {
                    Directory.CreateDirectory(_selectedRootPath);
                }
                catch (Exception ex)
                {
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Error", $"Failed to create directory: {ex.Message}");
                    return;
                }
            }

            DirectorySelected?.Invoke(this, _selectedRootPath);
        }

        private async void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select PocketMC Settings Backup File",
                Filter = "PocketMC Settings Backup (*.json)|*.json|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                string json = await File.ReadAllTextAsync(openFileDialog.FileName);
                SettingsBackupPackage package;
                try
                {
                    package = _settingsBackupService.DeserializePackage(json);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage(
                        "Invalid Backup File",
                        $"The selected file is not a valid PocketMC settings backup package.\n\nDetails: {ex.Message}",
                        DialogType.Error);
                    return;
                }

                var available = _settingsBackupService.GetAvailableCategories(package);
                var dialog = new SettingsBackupDialogWindow(SettingsBackupDialogMode.Restore, available)
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();
                if (!dialog.Confirmed)
                {
                    return;
                }

                var categoriesToRestore = dialog.GetSelectedCategories();
                var currentSettings = _settingsManager.Load();
                var mergedSettings = _settingsBackupService.RestoreFromPackage(currentSettings, package, categoriesToRestore);

                _settingsManager.Save(mergedSettings);
                _applicationState.ApplySettings(mergedSettings);

                // Apply visual theme live immediately
                if (Window.GetWindow(this) as MainWindow is MainWindow mainWin)
                {
                    mainWin.ApplyTheme();
                    mainWin.RequestMicaUpdate();
                }

                // If root path was restored, complete setup flow directly into Dashboard without restarting!
                if (!string.IsNullOrWhiteSpace(mergedSettings.AppRootPath))
                {
                    _selectedRootPath = mergedSettings.AppRootPath;
                    TxtSuggestedPath.Text = _selectedRootPath;
                    ContinueButton.IsEnabled = true;

                    if (!Directory.Exists(_selectedRootPath))
                    {
                        try { Directory.CreateDirectory(_selectedRootPath); } catch { }
                    }

                    DirectorySelected?.Invoke(this, _selectedRootPath);
                }
                else
                {
                    _dialogService.ShowMessage(
                        "Settings Restored",
                        "Your settings have been restored successfully. Please choose a home folder to finish setup.",
                        DialogType.Information);
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Restore Failed", $"Could not restore settings:\n{ex.Message}", DialogType.Error);
            }
        }
    }
}

