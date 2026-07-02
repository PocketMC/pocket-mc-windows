using PocketMC.Desktop.Features.Marketplace.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Application.Instances.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Marketplace;
using Wpf.Ui.Controls;
using System.Collections.ObjectModel;
using PocketMC.Desktop.Infrastructure.Security;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace PocketMC.Desktop.Features.Marketplace
{
    public partial class PluginBrowserPage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly ModrinthService _modrinth;
        private readonly CurseForgeService _curseForge;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MarketplaceFileInstaller _fileInstaller;
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
            DependencyResolverService resolver,
            AddonManifestService manifestService,
            IHttpClientFactory httpClientFactory,
            MarketplaceFileInstaller fileInstaller,
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
            _resolver = resolver;
            _manifestService = manifestService;
            _httpClientFactory = httpClientFactory;
            _fileInstaller = fileInstaller;
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
            KeyDown += PluginBrowserPage_KeyDown;
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
                string query = TxtSearch.Text ?? "";
                string sort = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "relevance";

                List<ModrinthHit> hits;
                string mcVersionArg = (_compat.Family == EngineFamily.Bedrock || _compat.Family == EngineFamily.Pocketmine) ? "" : _mcVersion;

                if (isCurseForge)
                {
                    // Standard CurseForge uses 432 for Java. If it's bedrock, we override type to '6945' inside the CurseForgeService search...
                    hits = await _curseForge.SearchAsync(_compat.Family == EngineFamily.Bedrock ? "6945" : _projectType, mcVersionArg, _compat.LoaderName, query, _currentOffset);
                }
                else
                {
                    hits = await _modrinth.SearchAsync(_projectType, mcVersionArg, _compat.CompatibleLoaderNames, sort, query, _currentOffset);
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
                            Provider = isCurseForge ? "CurseForge" : "Modrinth"
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
                    _ => _modrinth
                };
                var resolved = await _resolver.ResolveAsync(provider, _serverDir!, projectId, mcVersionArg, _compat.LoaderName, _compat);
                var rootResolved = resolved.FirstOrDefault();
                if (rootResolved == null || string.IsNullOrEmpty(rootResolved.DownloadUrl) || !string.IsNullOrEmpty(rootResolved.Error))
                {
                    string details = rootResolved?.Error ?? "No compatible version found.";
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowError(
                        "No compatible version found",
                        $"PocketMC could not find a compatible version of {vm.Title} for Minecraft {mcVersionArg}.{Environment.NewLine}{Environment.NewLine}Details: {details}");

                    vm.State = InstallState.NotInstalled;
                    vm.IsActionEnabled = true;
                    return;
                }

                if (vm.Provider == "CurseForge" && !ConfirmMarketplaceRisk(vm.Title, rootResolved.FileName ?? vm.Title))
                {
                    vm.IsActionEnabled = true;
                    vm.State = InstallState.NotInstalled;
                    return;
                }

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
                    bool isRoot = item.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase);
                    string? title = isRoot ? vm.Title : item.ProjectTitle;
                    string? icon = isRoot ? vm.IconUrl : null;
                    string? disp = isRoot ? vm.Title : item.ProjectTitle;

                    await InstallSingleFileAsync(item.DownloadUrl, item.FileName, vm.Provider, item.ProjectId, item.VersionId ?? "", item.Hash, item.HashType, title, icon, disp, item.ClientSide, item.ServerSide);
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

        private bool ConfirmMarketplaceRisk(string projectTitle, string fileName)
        {
            MarketplaceInstallRisk risk = MarketplaceInstallRiskAnalyzer.Analyze(providerName: "CurseForge", _projectType, projectTitle, fileName);
            if (!risk.RequiresConfirmation)
            {
                return true;
            }

            return PocketMC.Desktop.Infrastructure.AppDialog.Confirm(
                "CurseForge Compatibility Warning",
                string.Join(Environment.NewLine + Environment.NewLine, risk.Warnings) +
                Environment.NewLine + Environment.NewLine +
                "Install anyway?");
        }

        private async Task InstallSingleFileAsync(
            string url,
            string fileName,
            string providerName,
            string projectId,
            string versionId,
            string? hash,
            string? hashType,
            string? projectTitle = null,
            string? iconUrl = null,
            string? displayName = null,
            string? clientSide = null,
            string? serverSide = null)
        {
            if (_serverDir == null && !_isModpackMode) return;
            string safeFileName = MarketplaceDownloadPolicy.RequireCompatibleFileName(fileName, _compat, _isModpackMode);

            string destFile;
            if (_isModpackMode)
            {
                destFile = PathSafety.ValidateContainedPath(Path.GetTempPath(), safeFileName)
                    ?? throw new InvalidOperationException($"Invalid marketplace download file name '{safeFileName}'.");
            }
            else
            {
                string destDir = PathSafety.ValidateContainedPath(_serverDir!, _compat.PrimaryAddonSubDir)
                    ?? throw new InvalidOperationException($"Invalid add-on directory '{_compat.PrimaryAddonSubDir}'.");
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                destFile = PathSafety.ValidateContainedPath(destDir, safeFileName)
                    ?? throw new InvalidOperationException($"Invalid marketplace add-on file name '{safeFileName}'.");
            }

            await _fileInstaller.InstallAsync(url, destFile, hash, hashType);
            IReadOnlyList<string> metadataWarnings = MarketplaceArchiveInspector.InspectServerCompatibilityWarnings(destFile, isPlugin: _projectType.Contains("plugin"));
            if (metadataWarnings.Count > 0)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(
                    "Marketplace Compatibility Warning",
                    string.Join(Environment.NewLine + Environment.NewLine, metadataWarnings));
            }

            if (!_isModpackMode && MarketplaceArchiveInspector.IsClientOnlyAddon(destFile))
            {
                string disabledDestFile = destFile + ".disabled-by-pocketmc";
                if (File.Exists(disabledDestFile)) File.Delete(disabledDestFile);
                File.Move(destFile, disabledDestFile);
                destFile = disabledDestFile;
                safeFileName += ".disabled-by-pocketmc";
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
                await _manifestService.RegisterInstallAsync(
                    _serverDir,
                    providerName,
                    projectId,
                    versionId,
                    safeFileName,
                    projectTitle,
                    iconUrl,
                    displayName,
                    clientSide,
                    serverSide,
                    hash,
                    hashType,
                    _mcVersion,
                    _compat.LoaderName,
                    downloadUrl: url);
            }
        }

        private async void PluginBrowserPage_KeyDown(object sender, KeyEventArgs e)
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
    }
}

