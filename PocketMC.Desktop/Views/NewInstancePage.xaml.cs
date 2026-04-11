using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    public partial class NewInstancePage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly InstanceManager _instanceManager;
        private readonly VanillaProvider _vanillaProvider;
        private readonly PaperProvider _paperProvider;
        private readonly FabricProvider _fabricProvider;
        private readonly ForgeProvider _forgeProvider;
        private readonly ILogger<NewInstancePage> _logger;
        private bool _isCreating;
        private bool _isLoadingVersions;
        private bool _hasLoadedInitialVersions;
        private int _versionLoadRequestId;

        public NewInstancePage(
            IAppNavigationService navigationService,
            InstanceManager instanceManager,
            VanillaProvider vanillaProvider,
            PaperProvider paperProvider,
            FabricProvider fabricProvider,
            ForgeProvider forgeProvider,
            ILogger<NewInstancePage> logger)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _instanceManager = instanceManager;
            _vanillaProvider = vanillaProvider;
            _paperProvider = paperProvider;
            _fabricProvider = fabricProvider;
            _forgeProvider = forgeProvider;
            _logger = logger;

            Loaded += OnLoaded;
            SizeChanged += OnPageSizeChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveLayout();

            if (_hasLoadedInitialVersions)
            {
                UpdateCreateButtonState();
                return;
            }

            _hasLoadedInitialVersions = true;
            UpdateCreateButtonState();
            await LoadVersionsAsync(GetSelectedServerType());
        }

        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private async void CmbServerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbVersion == null)
            {
                return;
            }

            await LoadVersionsAsync(GetSelectedServerType());
        }

        private async void ChkShowSnapshots_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || CmbServerType == null)
            {
                return;
            }

            await LoadVersionsAsync(GetSelectedServerType());
        }

        private void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCreateButtonState();
            UpdateLoaderVersionSelector();
        }

        private void UpdateLoaderVersionSelector()
        {
            if (CmbVersion.SelectedItem is GameVersionWithLoaders gvl && gvl.LoaderVersions.Any())
            {
                LoaderVersionPanel.Visibility = Visibility.Visible;
                CmbLoaderVersion.ItemsSource = gvl.LoaderVersions;
                CmbLoaderVersion.SelectedIndex = 0;
            }
            else
            {
                LoaderVersionPanel.Visibility = Visibility.Collapsed;
                CmbLoaderVersion.ItemsSource = null;
            }
        }

        private void ChkAcceptEula_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCreateButtonState();
        }

        private async Task LoadVersionsAsync(string serverType)
        {
            int requestId = Interlocked.Increment(ref _versionLoadRequestId);

            try
            {
                ClearError();
                _isLoadingVersions = true;
                UpdateCreateButtonState();
                CmbVersion.IsEnabled = false;
                CmbVersion.ItemsSource = null;
                CmbVersion.SelectedItem = null;
                TxtVersionState.Text = $"Loading {serverType} versions...";

                if (serverType == "Forge")
                {
                    ChkShowSnapshots.IsEnabled = false;
                    ChkShowSnapshots.IsChecked = false;
                    ChkShowSnapshots.Opacity = 0.55;
                }
                else
                {
                    ChkShowSnapshots.IsEnabled = true;
                    ChkShowSnapshots.Opacity = 1.0;
                }

                IServerJarProvider provider = GetProvider(serverType);
                var versions = await provider.GetAvailableVersionsAsync();

                if (requestId != Volatile.Read(ref _versionLoadRequestId))
                {
                    return;
                }

                if (ChkShowSnapshots.IsChecked != true)
                {
                    versions = versions.Where(v => v.Type == "release").ToList();
                }

                CmbVersion.ItemsSource = versions;
                if (versions.Count > 0)
                {
                    CmbVersion.SelectedIndex = 0;
                    TxtVersionState.Text = $"{versions.Count} version{(versions.Count == 1 ? string.Empty : "s")} available for {serverType}.";
                }
                else
                {
                    TxtVersionState.Text = $"No versions are currently available for {serverType}.";
                }
            }
            catch (Exception ex)
            {
                if (requestId != Volatile.Read(ref _versionLoadRequestId))
                {
                    return;
                }

                TxtVersionState.Text = "Could not load versions right now.";
                ShowError($"Failed to load versions: {ex.Message}");
                _logger.LogWarning(ex, "Failed to load versions for server type {ServerType}.", serverType);
            }
            finally
            {
                if (requestId == Volatile.Read(ref _versionLoadRequestId))
                {
                    _isLoadingVersions = false;
                    CmbVersion.IsEnabled = true;
                    UpdateCreateButtonState();
                }
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_isCreating)
            {
                return;
            }

            NavigateToDashboard();
        }

        private async void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            ClearError();

            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                ShowError("Enter a server name before creating the instance.");
                return;
            }

            if (CmbVersion.SelectedItem is not MinecraftVersion selectedVersion)
            {
                ShowError("Select a Minecraft version before continuing.");
                return;
            }

            string serverType = GetSelectedServerType();
            string? createdInstancePath = null;
            string? createdFolderName = null;

            SetCreationState(true);

            try
            {
                var metadata = _instanceManager.CreateInstance(
                    TxtName.Text.Trim(),
                    TxtDescription.Text.Trim(),
                    serverType,
                    selectedVersion.Id);

                createdInstancePath = _instanceManager.GetInstancePath(metadata.Id);
                if (createdInstancePath == null)
                {
                    throw new InvalidOperationException("Instance directory could not be resolved after creation.");
                }

                createdFolderName = Path.GetFileName(createdInstancePath);
                string jarFile = serverType == "Forge" ? "forge-installer.jar" : "server.jar";
                string jarPath = Path.Combine(createdInstancePath, jarFile);

                IServerJarProvider provider = GetProvider(serverType);
                var progress = new Progress<DownloadProgress>(progress =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        PrgDownload.IsIndeterminate = progress.TotalBytes <= 0;
                        PrgDownload.Value = progress.Percentage;
                        TxtProgress.Text = progress.TotalBytes > 0
                            ? $"{FormatMegabytes(progress.BytesRead)} / {FormatMegabytes(progress.TotalBytes)}"
                            : $"{FormatMegabytes(progress.BytesRead)} downloaded";
                    });
                });

                TxtProgress.Text = "Downloading server jar...";
                
                string loaderVersion = (CmbLoaderVersion.SelectedItem as ModLoaderVersion)?.Version ?? "";

                if (serverType == "Fabric" && !string.IsNullOrEmpty(loaderVersion))
                {
                    await _fabricProvider.DownloadFabricJarAsync(selectedVersion.Id, loaderVersion, jarPath, progress);
                }
                else if (serverType == "Forge" && !string.IsNullOrEmpty(loaderVersion))
                {
                    string forgeJarPath = Path.Combine(createdInstancePath, "forge-installer.jar");
                    await _forgeProvider.DownloadForgeJarAsync(selectedVersion.Id, loaderVersion, forgeJarPath, progress);
                }
                else
                {
                    await provider.DownloadJarAsync(selectedVersion.Id, jarPath, progress);
                }

                if (ChkAcceptEula.IsChecked == true && createdFolderName != null)
                {
                    _instanceManager.AcceptEula(createdFolderName);
                }

                if (!NavigateToDashboard())
                {
                    SetCreationState(false);
                    _logger.LogWarning("Instance {InstanceName} was created, but PocketMC could not navigate back to the dashboard automatically.", TxtName.Text);
                    MessageBox.Show(
                        "The instance was created successfully, but PocketMC could not return to the Dashboard automatically.",
                        "Instance Created",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                CleanupFailedInstance(createdFolderName, createdInstancePath);
                SetCreationState(false);
                ShowError($"Could not create the instance: {ex.Message}");
                _logger.LogError(ex, "Failed to create a new instance named {InstanceName}.", TxtName.Text);
            }
        }

        private void SetCreationState(bool isCreating)
        {
            _isCreating = isCreating;
            InputsPanel.IsEnabled = !isCreating;
            BtnCancel.IsEnabled = !isCreating;
            ProgressOverlay.Visibility = isCreating ? Visibility.Visible : Visibility.Collapsed;

            if (isCreating)
            {
                BtnCreate.Content = "Creating...";
                PrgDownload.IsIndeterminate = true;
                PrgDownload.Value = 0;
                TxtProgress.Text = "Preparing server files...";
            }
            else
            {
                BtnCreate.Content = "Create and Download";
                PrgDownload.IsIndeterminate = false;
            }

            UpdateCreateButtonState();
        }

        private void UpdateCreateButtonState()
        {
            BtnCreate.IsEnabled =
                !_isCreating &&
                !_isLoadingVersions &&
                ChkAcceptEula.IsChecked == true &&
                CmbVersion.SelectedItem is MinecraftVersion;
        }

        private void ClearError()
        {
            TxtError.Text = string.Empty;
            ErrorCallout.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            TxtError.Text = message;
            ErrorCallout.Visibility = Visibility.Visible;
        }

        private string GetSelectedServerType() =>
            (CmbServerType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";

        private void CleanupFailedInstance(string? folderName, string? instancePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    _instanceManager.DeleteInstance(folderName);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(instancePath) && Directory.Exists(instancePath))
                {
                    Directory.Delete(instancePath, true);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up the partially created instance at {InstancePath}.", instancePath);
            }
        }

        private bool NavigateToDashboard()
        {
            return _navigationService.NavigateToDashboard();
        }

        private void MinecraftEulaLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open the Minecraft EULA link.");
                ShowError("PocketMC could not open the Minecraft EULA link right now.");
            }
        }

        private void UpdateResponsiveLayout()
        {
            if (ContentLayoutRoot == null || FormColumnDefinition == null || GapColumnDefinition == null || SideColumnDefinition == null)
            {
                return;
            }

            bool useStackedLayout = ActualWidth < 760;

            if (useStackedLayout)
            {
                FormColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
                GapColumnDefinition.Width = new GridLength(0);
                SideColumnDefinition.Width = new GridLength(0);

                Grid.SetRow(FormCard, 0);
                Grid.SetColumn(FormCard, 0);
                Grid.SetRow(ComplianceCard, 1);
                Grid.SetColumn(ComplianceCard, 0);
                ComplianceCard.Margin = new Thickness(0, 16, 0, 0);
            }
            else
            {
                FormColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
                GapColumnDefinition.Width = new GridLength(20);
                SideColumnDefinition.Width = new GridLength(268);

                Grid.SetRow(FormCard, 0);
                Grid.SetColumn(FormCard, 0);
                Grid.SetRow(ComplianceCard, 0);
                Grid.SetColumn(ComplianceCard, 2);
                ComplianceCard.Margin = new Thickness(0);
            }
        }

        private static string FormatMegabytes(long bytes)
        {
            double megabytes = bytes / 1024d / 1024d;
            return $"{megabytes:0.0} MB";
        }

        private IServerJarProvider GetProvider(string serverType)
        {
            if (string.Equals(serverType, "Paper", StringComparison.OrdinalIgnoreCase))
            {
                return _paperProvider;
            }

            if (string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase))
            {
                return _fabricProvider;
            }

            if (string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase))
            {
                return _forgeProvider;
            }

            return _vanillaProvider;
        }
    }
}
