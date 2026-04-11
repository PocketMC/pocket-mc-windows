using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Services;
using Wpf.Ui.Controls;
using System.Collections.ObjectModel;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace PocketMC.Desktop.Views
{
    public partial class PluginBrowserPage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly ModrinthService _modrinth = new();
        private readonly CurseForgeService _curseForge = new();
        private readonly string? _serverDir;
        private readonly string _mcVersion;
        private readonly string _projectType;
        private readonly bool _isModpackMode;
        private readonly Action? _onCompleted;
        private readonly ObservableCollection<ModrinthHit> _results = new();
        private int _currentOffset = 0;
        private System.Threading.CancellationTokenSource? _searchCts;

        public event Action<string>? OnModpackDownloaded;

        public PluginBrowserPage(
            IAppNavigationService navigationService,
            string? serverDir,
            string mcVersion,
            string projectType,
            Action? onCompleted = null)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _serverDir = serverDir;
            _mcVersion = mcVersion;
            _projectType = projectType;
            _isModpackMode = projectType.Contains("modpack");
            _onCompleted = onCompleted;

            ListResults.ItemsSource = _results;
            string baseTitle = _isModpackMode ? "Modpack Marketplace" : (_projectType.Contains("plugin") ? "Plugin Marketplace" : "Mod Marketplace");
            TxtTitle.Text = baseTitle;
            TxtMcVersion.Text = _mcVersion == "*" ? "All Versions" : $"Minecraft {_mcVersion}";
            
            // Set dynamic placeholder based on context
            if (_isModpackMode) TxtSearch.PlaceholderText = "Search modpacks...";
            else if (_projectType.Contains("plugin")) TxtSearch.PlaceholderText = "Search Spigot/Paper plugins...";
            else TxtSearch.PlaceholderText = "Search Forge/Fabric mods...";
            
            Loaded += async (s, e) => await RefreshResultsAsync();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.NavigateBack();
        }

        private async void RefreshList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                if (CmbSort != null && CmbSource != null)
                {
                    CmbSort.IsEnabled = (CmbSource.SelectedIndex == 0);
                }
                await RefreshResultsAsync();
            }
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

            try
            {
                bool isCurseForge = CmbSource.SelectedIndex == 1;
                string query = TxtSearch.Text ?? "";
                string sort = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "relevance";

                List<ModrinthHit> hits;
                if (isCurseForge)
                {
                    hits = await _curseForge.SearchAsync(_projectType, _mcVersion, query, _currentOffset);
                }
                else
                {
                    hits = await _modrinth.SearchAsync(_projectType, _mcVersion, sort, query, _currentOffset);
                }
                
                foreach (var hit in hits)
                {
                    if (!_results.Any(r => r.Slug == hit.Slug))
                        _results.Add(hit);
                }

                _currentOffset += hits.Count;
                BtnLoadMore.Visibility = hits.Count >= 20 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Search failed: {ex.Message}");
            }
            finally
            {
                ProgressSearching.Visibility = Visibility.Collapsed;
                ListResults.Visibility = Visibility.Visible;
            }
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
                bool isCurseForge = CmbSource.SelectedIndex == 1;
                ModrinthVersion? version;

                if (isCurseForge)
                {
                    version = await _curseForge.GetLatestVersionAsync(slug, _mcVersion == "*" ? "" : _mcVersion);
                }
                else
                {
                    version = await _modrinth.GetLatestVersionAsync(slug, _mcVersion == "*" ? "" : _mcVersion);
                }

                if (version == null || version.Files.Count == 0)
                {
                    System.Windows.MessageBox.Show($"No compatible version found on {(isCurseForge ? "CurseForge" : "Modrinth")}.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    _navigationService.NavigateBack();
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
