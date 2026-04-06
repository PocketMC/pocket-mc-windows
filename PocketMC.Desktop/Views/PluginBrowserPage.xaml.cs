using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PocketMC.Desktop.Services;
using Wpf.Ui.Controls;
using System.Collections.ObjectModel;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace PocketMC.Desktop.Views
{
    public partial class PluginBrowserPage : Page
    {
        private readonly ModrinthService _modrinth = new();
        private readonly string? _serverDir;
        private readonly string _mcVersion;
        private readonly string _projectType;
        private readonly bool _isModpackMode;
        private readonly Action? _onCompleted;
        private readonly ObservableCollection<ModrinthHit> _results = new();
        private int _currentOffset = 0;
        private bool _isChanging = false;
        private System.Threading.CancellationTokenSource? _searchCts;

        public event Action<string>? OnModpackDownloaded;

        public PluginBrowserPage(string? serverDir, string mcVersion, string projectType, Action? onCompleted = null)
        {
            InitializeComponent();
            _serverDir = serverDir;
            _mcVersion = mcVersion;
            _projectType = projectType;
            _isModpackMode = projectType.Contains("modpack");
            _onCompleted = onCompleted;

            ListResults.ItemsSource = _results;
            TxtTitle.Text = _isModpackMode ? "Modpack Marketplace" : (projectType.Contains("plugin") ? "Plugin Marketplace" : "Mod Marketplace");
            TxtMcVersion.Text = _mcVersion == "*" ? "All Versions" : $"Minecraft {_mcVersion}";
            
            Loaded += async (s, e) => await RefreshResultsAsync();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.NavigateBackFromDetail() == true) return;
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        }

        private async Task RefreshResultsAsync(bool append = false)
        {
            if (!append)
            {
                _currentOffset = 0;
                _results.Clear();
                ProgressSearching.Visibility = Visibility.Visible;
                ListResults.Visibility = Visibility.Collapsed;
            }

            var sort = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "relevance";
            var query = TxtSearch.Text ?? "";
            
            var hits = await _modrinth.SearchAsync(_projectType, _mcVersion, sort, query, _currentOffset);
            
            foreach (var hit in hits)
            {
                _results.Add(hit);
            }

            _currentOffset += hits.Count;
            BtnLoadMore.Visibility = hits.Count >= 20 ? Visibility.Visible : Visibility.Collapsed;
            
            ProgressSearching.Visibility = Visibility.Collapsed;
            ListResults.Visibility = Visibility.Visible;
        }

        private async void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) await RefreshResultsAsync();
        }

        private async void TxtSearch_TextChanged(Wpf.Ui.Controls.AutoSuggestBox sender, Wpf.Ui.Controls.AutoSuggestBoxTextChangedEventArgs e)
        {
            if (!IsLoaded) return;

            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            var token = _searchCts.Token;

            try
            {
                await Task.Delay(500, token); // Wait 500ms
                await RefreshResultsAsync();
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
        }

        private async void BtnLoadMore_Click(object sender, RoutedEventArgs e)
        {
            await RefreshResultsAsync(append: true);
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            string slug = btn.Tag.ToString() ?? "";
            btn.IsEnabled = false;
            btn.Content = "Installing...";

            try
            {
                var version = await _modrinth.GetLatestVersionAsync(slug, _mcVersion == "*" ? "" : _mcVersion);
                if (version == null || version.Files.Count == 0)
                {
                    System.Windows.MessageBox.Show("No compatible version found.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    btn.IsEnabled = true;
                    btn.Content = "Install";
                    return;
                }

                var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files[0];
                
                using var httpClient = new HttpClient();
                var data = await httpClient.GetByteArrayAsync(file.Url);

                if (_isModpackMode)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), file.FileName);
                    await File.WriteAllBytesAsync(tempFile, data);

                    OnModpackDownloaded?.Invoke(tempFile);

                    if (NavigationService.CanGoBack)
                    {
                        NavigationService.GoBack();
                    }
                    return;
                }

                if (_serverDir == null) return;
                
                string targetSubDir = _projectType.Contains("plugin") ? "plugins" : "mods";
                string destDir = Path.Combine(_serverDir, targetSubDir);
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                string destFile = Path.Combine(destDir, file.FileName);

                await File.WriteAllBytesAsync(destFile, data);

                btn.Content = "Installed";
                _onCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Install failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                btn.IsEnabled = true;
                btn.Content = "Install";
            }
        }
    }
}
