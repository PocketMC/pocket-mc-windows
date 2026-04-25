using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace.Models;
using Wpf.Ui.Controls;
using System.Collections.ObjectModel;
using PocketMC.Desktop.Models;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class PluginBrowserPage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly ModrinthService _modrinth;
        private readonly CurseForgeService _curseForge;
        private readonly PoggitService _poggit;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string? _serverDir;
        private readonly string _mcVersion;
        private readonly string _projectType;
        private readonly bool _isModpackMode;
        private readonly Action? _onCompleted;
        private readonly EngineCompatibility _compat;
        private readonly ObservableCollection<MarketplaceItemViewModel> _results = new();
        private int _currentOffset = 0;
        private System.Threading.CancellationTokenSource? _searchCts;

        public event Action<string>? OnModpackDownloaded;

        private readonly DependencyResolverService _resolver;
        private readonly AddonManifestService _manifestService;

        public PluginBrowserPage(
            IAppNavigationService navigationService,
            ModrinthService modrinth,
            CurseForgeService curseForge,
            PoggitService poggit,
            DependencyResolverService resolver,
            AddonManifestService manifestService,
            IHttpClientFactory httpClientFactory,
            string? serverDir,
            string mcVersion,
            string projectType,
            Action? onCompleted = null,
            EngineCompatibility? compat = null)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _modrinth = modrinth;
            _curseForge = curseForge;
            _poggit = poggit;
            _resolver = resolver;
            _manifestService = manifestService;
            _httpClientFactory = httpClientFactory;
            _serverDir = serverDir;
            _mcVersion = mcVersion;
            _projectType = projectType;
            _isModpackMode = projectType.Contains("modpack");
            _onCompleted = onCompleted;
            _compat = compat ?? new EngineCompatibility("Vanilla");
            ListResults.ItemsSource = _results;

            string baseTitle = _isModpackMode ? "Modpack Marketplace" : (_projectType.Contains("plugin") ? "Plugin Marketplace" : "Mod Marketplace");
            if (_compat.Family == EngineFamily.Bedrock) baseTitle = "Bedrock Add-Ons Marketplace";
            if (_compat.Family == EngineFamily.Pocketmine) 
            {
                baseTitle = "Pocketmine Plugins";
                CmbSource.Items.Clear();
                CmbSource.Items.Add(new ComboBoxItem { Content = "Poggit" });
                CmbSource.SelectedIndex = 0;
            }
            TxtTitle.Text = baseTitle;
            TxtMcVersion.Text = _mcVersion == "*" ? "All Versions" : $"Minecraft {_mcVersion}";

            if (_isModpackMode) TxtSearch.PlaceholderText = "Search modpacks...";
            else if (_compat.Family == EngineFamily.Bedrock) TxtSearch.PlaceholderText = "Search Bedrock Add-Ons...";
            else if (_compat.Family == EngineFamily.Pocketmine && _projectType.Contains("plugin")) TxtSearch.PlaceholderText = "Search Pocketmine plugins (*.phar)...";
            else if (_projectType.Contains("plugin")) TxtSearch.PlaceholderText = "Search Spigot/Paper plugins...";
            else if (_projectType.Contains("mod"))
            {
                string loaderLabel = string.IsNullOrWhiteSpace(_compat.LoaderName) ? "mods" : $"{ToDisplayLoader(_compat.LoaderName)} mods";
                TxtSearch.PlaceholderText = $"Search {loaderLabel}...";
            }
            else TxtSearch.PlaceholderText = "Search mods...";
            Loaded += async (s, e) => 
            {
                if (_serverDir != null)
                {
                    await _manifestService.SyncManifestAsync(_serverDir, _modrinth, _compat);
                }
                await RefreshResultsAsync();
            };
        }


        private static string ToDisplayLoader(string loader)
        {
            return loader.ToLowerInvariant() switch
            {
                "neoforge" => "NeoForge",
                "fabric" => "Fabric",
                "forge" => "Forge",
                "quilt" => "Quilt",
                _ => loader
            };
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
                bool isCurseForge = CmbSource.SelectedItem is ComboBoxItem c && c.Content.ToString() == "CurseForge";
                bool isPoggit = CmbSource.SelectedItem is ComboBoxItem pt && pt.Content.ToString() == "Poggit";
                string query = TxtSearch.Text ?? "";
                string sort = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "relevance";

                List<ModrinthHit> hits;
                string mcVersionArg = (_compat.Family == EngineFamily.Bedrock || _compat.Family == EngineFamily.Pocketmine) ? "" : _mcVersion;

                if (isCurseForge)
                {
                    // Standard CurseForge uses 432 for Java. If it's bedrock, we override type to '6945' inside the CurseForgeService search...
                    hits = await _curseForge.SearchAsync(_compat.Family == EngineFamily.Bedrock ? "6945" : _projectType, mcVersionArg, _compat.LoaderName, query, _currentOffset);
                }
                else if (isPoggit)
                {
                    hits = await _poggit.SearchAsync(query, _currentOffset);
                }
                else
                {
                    hits = await _modrinth.SearchAsync(_projectType, mcVersionArg, _compat.LoaderName, sort, query, _currentOffset);
                }

                foreach (var hit in hits)
                {
                    if (!_results.Any(r => r.Slug == hit.Slug))
                    {
                        var vm = new MarketplaceItemViewModel
                        {
                            Title = hit.Title,
                            Description = hit.Description,
                            IconUrl = hit.IconUrl,
                            Downloads = hit.Downloads,
                            Slug = hit.Slug,
                            ProjectId = hit.ProjectId,
                            Provider = isCurseForge ? "CurseForge" : (isPoggit ? "Poggit" : "Modrinth")
                        };

                        if (_serverDir != null)
                        {
                            bool installed = await _manifestService.IsInstalledAsync(_serverDir, vm.Provider, vm.ProjectId, _compat);
                            vm.State = installed ? InstallState.Installed : InstallState.NotInstalled;
                        }

                        _results.Add(vm);
                    }
                }

                _currentOffset += hits.Count;
                BtnLoadMore.Visibility = hits.Count >= 20 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Search Error", $"Search failed: {ex.Message}");
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
            var btn = (System.Windows.Controls.Button)sender;
            var vm = (MarketplaceItemViewModel)btn.DataContext;
            vm.IsActionEnabled = false;
            vm.State = InstallState.Installing;

            try
            {
                string projectId = vm.ProjectId;
                if (string.IsNullOrEmpty(projectId)) projectId = vm.Slug;

                string mcVersionArg = (_compat.Family == EngineFamily.Bedrock || _compat.Family == EngineFamily.Pocketmine) ? "" : (_mcVersion == "*" ? "" : _mcVersion);
                
                IAddonProvider provider = vm.Provider switch
                {
                    "CurseForge" => _curseForge,
                    "Poggit" => _poggit,
                    _ => _modrinth
                };

                // Poggit doesn't support recursive deps yet
                if (vm.Provider == "Poggit")
                {
                    var pVersion = await _poggit.GetLatestVersionAsync(projectId);
                    if (pVersion == null) 
                    { 
                        vm.IsActionEnabled = true; 
                        vm.State = InstallState.NotInstalled; 
                        return; 
                    }
                    await InstallSingleFileAsync(pVersion.DownloadUrl, pVersion.FileName, "Poggit", pVersion.ProjectId, pVersion.Id);
                    
                    vm.State = InstallState.Installed;
                    vm.IsActionEnabled = true;
                    return;
                }

                if (_serverDir == null) return;
                var resolved = await _resolver.ResolveAsync(provider, _serverDir, projectId, mcVersionArg, _compat.LoaderName, _compat);
                
                // --- 2. User Confirmation ---
                var confVm = new DependencyConfirmationViewModel(resolved);
                var win = new DependencyConfirmationWindow(confVm) { Owner = Window.GetWindow(this) };
                if (win.ShowDialogWithResult() != true)
                {
                    vm.IsActionEnabled = true;
                    vm.State = InstallState.NotInstalled;
                    return;
                }

                // --- 3. Batch Installation ---
                foreach (var item in resolved.Where(d => d.IsSelected))
                {
                    await InstallSingleFileAsync(item.DownloadUrl, item.FileName, vm.Provider, item.ProjectId, item.VersionId ?? "");
                }

                vm.State = InstallState.Installed;
                vm.IsActionEnabled = true;
                _onCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Error", "Install failed: " + ex.Message);
                vm.State = InstallState.Failed;
                vm.IsActionEnabled = true;
            }
        }

        private async Task InstallSingleFileAsync(string url, string fileName, string providerName, string projectId, string versionId)
        {
            if (_serverDir == null && !_isModpackMode) return;

            using var httpClient = _httpClientFactory.CreateClient("PocketMC.Downloads");
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            string destFile;
            if (_isModpackMode)
            {
                destFile = Path.Combine(Path.GetTempPath(), fileName);
            }
            else
            {
                string destDir = Path.Combine(_serverDir!, _compat.PrimaryAddonSubDir);
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                destFile = Path.Combine(destDir, fileName);
            }

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            if (_isModpackMode)
            {
                OnModpackDownloaded?.Invoke(destFile);
                _navigationService.NavigateBack();
                return;
            }

            // Register in manifest if not modpack
            if (_serverDir != null)
            {
                await _manifestService.RegisterInstallAsync(_serverDir, providerName, projectId, versionId, fileName);
            }
        }
    }
}
