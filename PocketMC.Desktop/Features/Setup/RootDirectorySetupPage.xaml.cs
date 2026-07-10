using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Setup
{
    public partial class RootDirectorySetupPage : Page
    {
        public event EventHandler<string>? DirectorySelected;
        private string? _selectedRootPath;

        public RootDirectorySetupPage()
        {
            InitializeComponent();

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
    }
}

