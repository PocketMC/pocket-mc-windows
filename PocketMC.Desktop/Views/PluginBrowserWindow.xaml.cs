using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Services;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace PocketMC.Desktop.Views
{
    public partial class PluginBrowserWindow : FluentWindow
    {
        private readonly ModrinthService _modrinth = new();
        private readonly string _serverDir;
        private readonly string _mcVersion;
        private readonly string _projectType; // "project_type:plugin" or "project_type:mod"

        public PluginBrowserWindow(string serverDir, string mcVersion, string projectType)
        {
            InitializeComponent();
            _serverDir = serverDir;
            _mcVersion = mcVersion;
            _projectType = projectType;

            TxtTitle.Text = projectType.Contains("plugin") ? "Plugin Marketplace" : "Mod Marketplace";
            TxtMcVersion.Text = $"Minecraft {_mcVersion}";
            
            Loaded += async (s, e) => await RefreshResultsAsync();
        }

        private async Task RefreshResultsAsync()
        {
            ProgressSearching.Visibility = Visibility.Visible;
            ListResults.Visibility = Visibility.Collapsed;

            var sort = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "relevance";
            var hits = await _modrinth.SearchAsync(_projectType, _mcVersion, sort);
            
            ListResults.ItemsSource = hits;
            
            ProgressSearching.Visibility = Visibility.Collapsed;
            ListResults.Visibility = Visibility.Visible;
        }

        private async void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) await RefreshResultsAsync();
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            string slug = btn.Tag.ToString() ?? "";
            btn.IsEnabled = false;
            btn.Content = "Installing...";

            try
            {
                var version = await _modrinth.GetLatestVersionAsync(slug, _mcVersion);
                if (version == null || version.Files.Count == 0)
                {
                    System.Windows.MessageBox.Show("No compatible version found for " + _mcVersion, "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Pick the primary file or the first one
                var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files[0];
                
                string targetSubDir = _projectType.Contains("plugin") ? "plugins" : "mods";
                string destDir = Path.Combine(_serverDir, targetSubDir);
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                string destFile = Path.Combine(destDir, file.FileName);

                using var httpClient = new HttpClient();
                var data = await httpClient.GetByteArrayAsync(file.Url);
                await File.WriteAllBytesAsync(destFile, data);

                TxtStatus.Text = $"Successfully installed {file.FileName}";
                btn.Content = "Installed";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Install failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                btn.IsEnabled = true;
                btn.Content = "Install";
            }
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
