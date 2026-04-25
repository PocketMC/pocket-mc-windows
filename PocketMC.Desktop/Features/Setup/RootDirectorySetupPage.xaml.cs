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
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
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

            string defaultParentDirectory = RootDirectorySetupHelper.GetDefaultParentDirectory();
            _selectedRootPath = Path.Combine(defaultParentDirectory, RootDirectorySetupHelper.SuggestedFolderName);
            TxtSuggestedFolderName.Text = RootDirectorySetupHelper.SuggestedFolderName;
            TxtSuggestedPath.Text = _selectedRootPath;
        }

        private void BtnSelectDirectory_Click(object sender, RoutedEventArgs e)
        {
            string defaultParentDirectory = RootDirectorySetupHelper.GetDefaultParentDirectory();
            string suggestedFullPath = Path.Combine(defaultParentDirectory, RootDirectorySetupHelper.SuggestedFolderName);

            if (!Directory.Exists(suggestedFullPath))
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
