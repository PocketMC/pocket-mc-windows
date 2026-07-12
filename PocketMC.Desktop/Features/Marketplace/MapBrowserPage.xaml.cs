using PocketMC.Desktop.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PocketMC.Application.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Infrastructure.Marketplace;
using PocketMC.Application.Services.Mods;
using System.Collections.ObjectModel;
using PocketMC.Domain.Storage;
using PocketMC.Desktop.Infrastructure;

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
        private bool _isLoadingMore = false;
        private bool _hasMoreResults = true;
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
            KeyDown += MapBrowserPage_KeyDown;
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
                
                int sortField = 2; // Popularity default
                if (CmbSort.SelectedItem is System.Windows.Controls.ComboBoxItem sortItem && sortItem.Tag is string sortStr && int.TryParse(sortStr, out int sortVal))
                {
                    sortField = sortVal;
                }

                int? categoryId = null;
                if (CmbCategory.SelectedItem is System.Windows.Controls.ComboBoxItem catItem && catItem.Tag is string catStr && int.TryParse(catStr, out int catVal))
                {
                    categoryId = catVal;
                }

                // Maps/Worlds class ID is 17 in CurseForge API
                var hits = await _curseForge.SearchAsync("project_type:world", _mcVersion, "", query, _currentOffset, sortField, "desc", categoryId);

                foreach (var hit in hits)
                {
                    if (!_results.Any(r => r.Slug == hit.Slug))
                        _results.Add(hit);
                }

                _currentOffset += hits.Count;
                _hasMoreResults = hits.Count >= 20;
                ProgressLoadMore.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Search Error", $"Search failed: {ex.Message}");
            }
            finally
            {
                ProgressSearching.Visibility = Visibility.Collapsed;
                ListResults.Visibility = Visibility.Visible;
                _isLoadingMore = false;
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

        private async void ResultsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange > 0)
            {
                var scrollViewer = (ScrollViewer)sender;
                if (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 50)
                {
                    if (!_isLoadingMore && _hasMoreResults)
                    {
                        _isLoadingMore = true;
                        ProgressLoadMore.Visibility = Visibility.Visible;
                        await RefreshResultsAsync(append: true);
                    }
                }
            }
        }

        private void ShowCurseForgeApiKeyDialog()
        {
            bool goToSettings = PocketMC.Desktop.Infrastructure.AppDialog.Confirm(
                "CurseForge API Key Required",
                "To search and install addons from CurseForge, you must configure a CurseForge API key in Settings.\n\n" +
                "You can get a free API key at:\nhttps://console.curseforge.com/#/api-keys/\n\n" +
                "Would you like to open Settings to configure it now?");

            if (goToSettings)
            {
                _navigationService.NavigateToShellPage(typeof(PocketMC.Desktop.Features.Setup.AppSettingsPage));
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string slug = btn.Tag?.ToString() ?? "";

            if (btn.DataContext is ModrinthHit hit && (string.IsNullOrEmpty(slug) || hit.Title.Contains("API Error") || hit.Title.Contains("Key Required")))
            {
                ShowCurseForgeApiKeyDialog();
                return;
            }

            btn.IsEnabled = false;
            btn.Content = "Downloading...";

            var cts = new System.Threading.CancellationTokenSource();
            string? destFile = null;
            bool success = false;

            try
            {
                var version = await _curseForge.GetLatestVersionAsync(slug, _mcVersion == "*" ? "" : _mcVersion, "");

                if (version == null || version.Files.Count == 0)
                {
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning("Not Found", "No compatible world version found on CurseForge.");
                    btn.IsEnabled = true;
                    btn.Content = "Import";
                    return;
                }

                var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files[0];
                string safeFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(file.FileName);
                destFile = Path.Combine(Path.GetTempPath(), safeFileName);

                var dialog = new ProgressDialogWindow(
                    "Downloading Map",
                    $"Downloading {file.FileName}...",
                    async (progress, ct) =>
                    {
                        using var httpClient = _httpClientFactory.CreateClient("PocketMC.Downloads");
                        using var response = await httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var lastReportTime = DateTime.UtcNow;

                        await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                        await using (var fileStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
                        {
                            var buffer = new byte[81920];
                            long totalRead = 0;
                            int read;
                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read, ct);
                                totalRead += read;

                                var now = DateTime.UtcNow;
                                var elapsed = now - lastReportTime;
                                if (elapsed.TotalMilliseconds >= 500 || totalRead == totalBytes)
                                {
                                    string sizeText = totalBytes > 0
                                        ? $"{FormatBytes(totalRead)} / {FormatBytes(totalBytes)}"
                                        : $"{FormatBytes(totalRead)}";

                                    double percentage = totalBytes > 0
                                        ? (double)totalRead / totalBytes * 100.0
                                        : -1.0;

                                    progress.Report(new ProgressDialogUpdate
                                    {
                                        Percentage = percentage,
                                        Message = sizeText
                                    });

                                    lastReportTime = now;
                                }
                            }
                        }
                        success = true;
                    },
                    cts)
                {
                    Owner = Window.GetWindow(this)
                };

                dialog.ShowDialog();

                if (success)
                {
                    OnMapDownloaded?.Invoke(destFile);
                    _navigationService.NavigateBack();
                }
                else
                {
                    if (!string.IsNullOrEmpty(destFile) && File.Exists(destFile))
                    {
                        try { File.Delete(destFile); } catch { }
                    }
                    btn.IsEnabled = true;
                    btn.Content = "Import";
                }
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Error", "Download failed: " + ex.Message);
                if (!string.IsNullOrEmpty(destFile) && File.Exists(destFile))
                {
                    try { File.Delete(destFile); } catch { }
                }
                btn.IsEnabled = true;
                btn.Content = "Import";
            }
        }

        private async void MapBrowserPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                TxtSearch.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
            {
                await RefreshResultsAsync();
                e.Handled = true;
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(TxtSearch.Text))
                {
                    TxtSearch.Text = string.Empty;
                }
                else
                {
                    Keyboard.ClearFocus();
                }
                e.Handled = true;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
