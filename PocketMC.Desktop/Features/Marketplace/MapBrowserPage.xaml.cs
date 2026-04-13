using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Marketplace;
using System.Collections.ObjectModel;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class MapBrowserPage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly CurseForgeService _curseForge;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _mcVersion;
        private readonly ObservableCollection<ModrinthHit> _results = new();
        private int _currentOffset = 0;
        private System.Threading.CancellationTokenSource? _searchCts;

        public event Action<string>? OnMapDownloaded;

        public MapBrowserPage(
            IAppNavigationService navigationService,
            CurseForgeService curseForge,
            IHttpClientFactory httpClientFactory,
            string mcVersion)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _curseForge = curseForge;
            _httpClientFactory = httpClientFactory;
            _mcVersion = mcVersion;

            ListResults.ItemsSource = _results;
            TxtMcVersion.Text = _mcVersion == "*" ? "All Versions" : $"Minecraft {_mcVersion}";

            Loaded += async (s, e) => await RefreshResultsAsync();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.NavigateBack();
        }

        private void BtnWebBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.minecraftmaps.com/search",
                    UseShellExecute = true
                });
            }
            catch { /* Ignore */ }
        }

        private async void RefreshList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
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
                string query = TxtSearch.Text ?? "";

                // Maps/Worlds class ID is 17 in CurseForge API
                var hits = await _curseForge.SearchAsync("project_type:world", _mcVersion, query, _currentOffset);

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
                MessageBox.Show($"Search failed: {ex.Message}");
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
                await Task.Delay(500, token);
                await RefreshResultsAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void BtnLoadMore_Click(object sender, RoutedEventArgs e)
        {
            await RefreshResultsAsync(append: true);
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string slug = btn.Tag.ToString() ?? "";
            btn.IsEnabled = false;
            btn.Content = "Downloading...";

            try
            {
                var version = await _curseForge.GetLatestVersionAsync(slug, _mcVersion == "*" ? "" : _mcVersion);

                if (version == null || version.Files.Count == 0)
                {
                    MessageBox.Show($"No compatible world version found on CurseForge.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    btn.IsEnabled = true;
                    btn.Content = "Import";
                    return;
                }

                var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files[0];
                using var httpClient = _httpClientFactory.CreateClient("PocketMC.Downloads");
                using var response = await httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                string destFile = Path.Combine(Path.GetTempPath(), file.FileName);

                await using (var contentStream = await response.Content.ReadAsStreamAsync())
                await using (var fileStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
                {
                    await contentStream.CopyToAsync(fileStream);
                }

                OnMapDownloaded?.Invoke(destFile);
                _navigationService.NavigateBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Download failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                btn.IsEnabled = true;
                btn.Content = "Import";
            }
        }
    }
}
