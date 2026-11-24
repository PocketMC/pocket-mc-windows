using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Domain.Exceptions;
using PocketMC.Desktop.Localization;
using Microsoft.Extensions.DependencyInjection;
using PocketMC.Application.Services.Shell;
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
    public enum MarketplacePackKind { World, DataPack, ResourcePack }

    public partial class MapBrowserPage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly CurseForgeService _curseForge;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ModrinthService _modrinth;
        private readonly MarketplacePackKind _kind;
        private readonly Func<string, string, bool>? _isInstalled;
        private readonly string _mcVersion;
        private readonly string? _serverType;
        private readonly ObservableCollection<ModrinthHit> _results = new();
        private int _currentOffset = 0;
        private bool _isLoadingMore = false;
        private bool _hasMoreResults = true;
        private System.Threading.CancellationTokenSource? _searchCts;

        public event Action<string>? OnMapDownloaded;

        /// <summary>Raised for resource packs: (download url, sha1 or null). Servers fetch the pack themselves.</summary>
        public event Action<string, string?>? OnResourcePackSelected;

        /// <summary>Raised for data packs: (provider name, project id). The caller downloads and tracks the pack.</summary>
        public event Action<string, string>? OnDataPackSelected;

        private bool UseModrinth => _kind != MarketplacePackKind.World && CmbSource.SelectedIndex == 0;
        private string ModrinthType => _kind == MarketplacePackKind.DataPack ? "project_type:datapack" : "project_type:resourcepack";

        public MapBrowserPage(
            IAppNavigationService navigationService,
            CurseForgeService curseForge,
            ModrinthService modrinth,
            IHttpClientFactory httpClientFactory,
            string mcVersion,
            MarketplacePackKind kind = MarketplacePackKind.World,
            string? serverType = null,
            Func<string, string, bool>? isInstalled = null)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _curseForge = curseForge;
            _modrinth = modrinth;
            _kind = kind;
            _isInstalled = isInstalled;
            _httpClientFactory = httpClientFactory;
            _mcVersion = mcVersion;
            _serverType = serverType;

            ListResults.ItemsSource = _results;
            if (kind == MarketplacePackKind.World)
            {
                CmbSource.IsEnabled = false;
            }
            else
            {
                bool data = kind == MarketplacePackKind.DataPack;
                TxtTitle.Text = data ? L.T("Data Packs Browser") : L.T("Resource Packs Browser");
                TxtSearch.PlaceholderText = data ? L.T("Search data packs...") : L.T("Search resource packs...");
                CmbCategory.Visibility = Visibility.Collapsed;
                LblCategory.Visibility = Visibility.Collapsed;
                CmbSource.SelectedIndex = 0;
            }
            TxtMcVersion.Text = MarketplaceFilterFormatter.FormatCompatibilityLabel(_serverType, _mcVersion);
            TxtMcVersion.ToolTip = MarketplaceFilterFormatter.FormatCompatibilityTooltip(_serverType, _mcVersion);
            IconMcVersion.ToolTip = TxtMcVersion.ToolTip;

            Loaded += async (s, e) => await RefreshResultsAsync();
            ScrollViewerHelper.EnableMouseWheelScrolling(this, ResultsScrollViewer);
            Unloaded += (s, e) => ScrollViewerHelper.DisableMouseWheelScrolling(this);
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
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                CurseForgeKeyPanel.Visibility = Visibility.Collapsed;
                ResultsScrollViewer.Visibility = Visibility.Visible;
                ListResults.Visibility = Visibility.Collapsed;
            }

            CurseForgeApiKeyException? keyError = null;

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

                List<ModrinthHit> hits;
                if (UseModrinth)
                {
                    string index = sortField switch { 2 or 6 => "downloads", 3 => "updated", _ => "relevance" };
                    hits = await _modrinth.SearchAsync(ModrinthType, _mcVersion, Array.Empty<string>(), index, query, _currentOffset);
                }
                else
                {
                    // CurseForge class IDs: worlds 17, data packs 6945, resource packs 12
                    string cfType = _kind switch { MarketplacePackKind.DataPack => "6945", MarketplacePackKind.ResourcePack => "12", _ => "project_type:world" };
                    hits = await _curseForge.SearchAsync(cfType, _mcVersion, "", query, _currentOffset, sortField, "desc", categoryId);
                }

                foreach (var hit in hits)
                {
                    if (!_results.Any(r => r.Slug == hit.Slug))
                    {
                        // CurseForge hits carry the project id in Slug; Modrinth has a separate project id.
                        hit.IsInstalled = _isInstalled?.Invoke(UseModrinth ? "Modrinth" : "CurseForge",
                            string.IsNullOrEmpty(hit.ProjectId) ? hit.Slug : hit.ProjectId) ?? false;
                        _results.Add(hit);
                    }
                }

                _currentOffset += hits.Count;
                _hasMoreResults = hits.Count >= 20;
                ProgressLoadMore.Visibility = Visibility.Collapsed;
            }
            catch (CurseForgeApiKeyException ex)
            {
                keyError = ex;
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Search Error", L.F("Search failed: {0}", ex.Message));
            }
            finally
            {
                ProgressSearching.Visibility = Visibility.Collapsed;

                if (keyError != null)
                {
                    CurseForgeKeyPanel.Visibility = Visibility.Visible;
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                    ResultsScrollViewer.Visibility = Visibility.Collapsed;
                    ListResults.Visibility = Visibility.Collapsed;

                    if (keyError.IsMissingKey)
                    {
                        TxtCurseForgeKeyTitle.Text = L.T("CurseForge API Key Required");
                        TxtCurseForgeKeyDetail.Text = L.T("CurseForge requires a free API key to search and download worlds. Enter your key to continue.");
                    }
                    else
                    {
                        TxtCurseForgeKeyTitle.Text = L.T("Invalid CurseForge API Key");
                        TxtCurseForgeKeyDetail.Text = L.T("Your CurseForge API key was rejected (403 Forbidden). Please check that your key is entered correctly and has active permissions.");
                    }
                }
                else
                {
                    CurseForgeKeyPanel.Visibility = Visibility.Collapsed;
                    bool isEmpty = _results.Count == 0;
                    EmptyStatePanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
                    ResultsScrollViewer.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
                    ListResults.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

                    if (isEmpty)
                    {
                        string query = TxtSearch.Text?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(query))
                        {
                            TxtEmptyStateDetail.Text = L.F("No worlds or maps matched \"{0}\". Try checking for spelling errors or clearing the category filter.", query);
                        }
                        else
                        {
                            TxtEmptyStateDetail.Text = L.T("No worlds or maps found for the selected criteria. Try changing the category filter or search terms.");
                        }
                    }
                }

                _isLoadingMore = false;
            }
        }

        private async void BtnResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _searchCts?.Cancel();
            TxtSearch.Text = "";
            CmbCategory.SelectedIndex = 0;
            CmbSort.SelectedIndex = 0;
            await RefreshResultsAsync();
        }

        private async void BtnConfigureCurseForgeKey_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CurseForgeApiKeyDialogWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ApiKey))
            {
                if (System.Windows.Application.Current is App app)
                {
                    var appState = app.Services.GetService<ApplicationState>();
                    var settingsManager = app.Services.GetService<PocketMC.Infrastructure.Configuration.SettingsManager>();
                    if (appState != null)
                    {
                        appState.Settings.CurseForgeApiKey = dialog.ApiKey;
                        settingsManager?.Save(appState.Settings);
                    }
                }
                await RefreshResultsAsync();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
            e.Handled = true;
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

            if (_kind == MarketplacePackKind.DataPack)
            {
                OnDataPackSelected?.Invoke(UseModrinth ? "Modrinth" : "CurseForge", slug);
                _navigationService.NavigateBack();
                return;
            }

            btn.IsEnabled = false;
            // Resource packs are only linked in server.properties, nothing is downloaded.
            btn.Content = _kind == MarketplacePackKind.ResourcePack ? L.T("Applying...") : L.T("Downloading...");

            var cts = new System.Threading.CancellationTokenSource();
            string? destFile = null;
            bool success = false;

            try
            {
                string mc = _mcVersion == "*" ? "" : _mcVersion;
                ModrinthVersion? version = null;
                if (UseModrinth)
                {
                    foreach (var cand in ModrinthService.BuildMinecraftVersionCandidates(mc))
                    {
                        version = await _modrinth.GetLatestVersionAsync(slug, cand);
                        if (version != null) break;
                    }
                }
                else
                {
                    version = await _curseForge.GetLatestVersionAsync(slug, mc, "");
                }

                if (version == null || version.Files.Count == 0)
                {
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(L.T("Not Found"), L.T("No compatible world version found on CurseForge."));
                    btn.IsEnabled = true;
                    btn.Content = L.T("Import");
                    return;
                }

                var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files[0];

                if (_kind == MarketplacePackKind.ResourcePack)
                {
                    file.Hashes.TryGetValue("sha1", out string? sha1);
                    OnResourcePackSelected?.Invoke(file.Url, sha1);
                    _navigationService.NavigateBack();
                    return;
                }
                string safeFileName = MarketplaceFileNameSanitizer.RequireSafeFileName(file.FileName);
                destFile = Path.Combine(Path.GetTempPath(), safeFileName);

                var dialog = new ProgressDialogWindow(
                    "Downloading Map",
                    L.F("Downloading {0}...", file.FileName),
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
                    btn.Content = L.T("Import");
                }
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError(L.T("Error"), L.F("Download failed: {0}", ex.Message));
                if (!string.IsNullOrEmpty(destFile) && File.Exists(destFile))
                {
                    try { File.Delete(destFile); } catch { }
                }
                btn.IsEnabled = true;
                btn.Content = L.T("Import");
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
