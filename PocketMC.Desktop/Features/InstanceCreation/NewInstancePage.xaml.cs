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
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;

using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Marketplace;
using PocketMC.Desktop.Features.Mods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace PocketMC.Desktop.Features.InstanceCreation
{
    public partial class NewInstancePage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly InstanceManager _instanceManager;
        private readonly InstanceRegistry _registry;
        private readonly VanillaProvider _vanillaProvider;
        private readonly PaperProvider _paperProvider;
        private readonly FabricProvider _fabricProvider;
        private readonly ForgeProvider _forgeProvider;
        private readonly NeoForgeProvider _neoForgeProvider;
        private readonly BedrockBdsProvider _bedrockProvider;
        private readonly PocketmineProvider _pocketmineProvider;
        private readonly GeyserProvisioningService _geyserProvisioning;
        private readonly DownloaderService _downloader;
        private readonly ILogger<NewInstancePage> _logger;
        private bool _isCreating;
        private bool _isLoadingVersions;
        private bool _hasLoadedInitialVersions;
        private int _versionLoadRequestId;

        public NewInstancePage(
            IAppNavigationService navigationService,
            InstanceManager instanceManager,
            InstanceRegistry registry,
            VanillaProvider vanillaProvider,
            PaperProvider paperProvider,
            FabricProvider fabricProvider,
            ForgeProvider forgeProvider,
            NeoForgeProvider neoForgeProvider,
            BedrockBdsProvider bedrockProvider,
            PocketmineProvider pocketmineProvider,
            GeyserProvisioningService geyserProvisioning,
            DownloaderService downloader,
            ILogger<NewInstancePage> logger)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _instanceManager = instanceManager;
            _registry = registry;
            _vanillaProvider = vanillaProvider;
            _paperProvider = paperProvider;
            _fabricProvider = fabricProvider;
            _forgeProvider = forgeProvider;
            _neoForgeProvider = neoForgeProvider;
            _bedrockProvider = bedrockProvider;
            _pocketmineProvider = pocketmineProvider;
            _geyserProvisioning = geyserProvisioning;
            _downloader = downloader;
            _logger = logger;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {

            if (_hasLoadedInitialVersions)
            {
                UpdateCreateButtonState();
                return;
            }

            _hasLoadedInitialVersions = true;
            string serverType = GetSelectedServerType();
            UpdateAddonPanelVisibility(serverType);
            UpdateCreateButtonState();
            await LoadVersionsAsync(serverType);
        }



        private async void CmbServerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbVersion == null)
            {
                return;
            }

            string serverType = GetSelectedServerType();
            if (serverType == "Forge" || serverType == "NeoForge")
            {
                TxtForgeWarning.Text = $"⚠ {serverType} support is in beta. First launch runs the installer automatically. This may take several minutes.";
                TxtForgeWarning.Visibility = Visibility.Visible;
            }
            else
            {
                TxtForgeWarning.Visibility = Visibility.Collapsed;
            }

            UpdateAddonPanelVisibility(serverType);

            await LoadVersionsAsync(serverType);
        }

        private void UpdateAddonPanelVisibility(string serverType)
        {
            if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) || 
                serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) ||
                serverType.StartsWith("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                AddonPanel.Visibility = Visibility.Collapsed;
                ChkEnableGeyser.IsChecked = false;
            }
            else
            {
                AddonPanel.Visibility = Visibility.Visible;
            }
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

                if (serverType == "Forge" || serverType == "NeoForge")
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

                IServerSoftwareProvider provider = GetProvider(serverType);
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

                createdInstancePath = _registry.GetPath(metadata.Id);
                if (createdInstancePath == null)
                {
                    throw new InvalidOperationException("Instance directory could not be resolved after creation.");
                }

                createdFolderName = Path.GetFileName(createdInstancePath);
                string jarFile = "server.jar";
                if (serverType == "Forge" || serverType == "NeoForge") jarFile = "installer.jar";
                else if (serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase)) jarFile = "PocketMine-MP.phar";
                string jarPath = Path.Combine(createdInstancePath, jarFile);

                IServerSoftwareProvider provider = GetProvider(serverType);
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

                TxtProgress.Text = "Downloading server software...";

                string loaderVersion = (CmbLoaderVersion.SelectedItem as ModLoaderVersion)?.Version ?? "";

                bool isBedrock = serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase);

                if (isBedrock)
                {
                    // Ensure the instance directory exists before writing anything into it.
                    Directory.CreateDirectory(createdInstancePath);

                    // Use system temp dir — guaranteed writable, not inside the instance path.
                    string tempZip = Path.Combine(Path.GetTempPath(), $"pocketmc-bds-{Guid.NewGuid():N}.zip");
                    try
                    {
                        // DownloadSoftwareAsync writes the ZIP to tempZip, then we extract.
                        await provider.DownloadSoftwareAsync(selectedVersion.Id, tempZip, progress);
                        Dispatcher.Invoke(() => TxtProgress.Text = "Extracting Bedrock server files...");
                        await _downloader.ExtractZipAsync(tempZip, createdInstancePath, progress);
                    }
                    finally
                    {
                        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                    }
                }

                else if (serverType == "Fabric" && !string.IsNullOrEmpty(loaderVersion))
                {
                    await _fabricProvider.DownloadFabricJarAsync(selectedVersion.Id, loaderVersion, jarPath, progress);
                }
                else if (serverType == "Forge" && !string.IsNullOrEmpty(loaderVersion))
                {
                    string forgeJarPath = Path.Combine(createdInstancePath, "installer.jar");
                    await _forgeProvider.DownloadForgeJarAsync(selectedVersion.Id, loaderVersion, forgeJarPath, progress);
                }
                else if (serverType == "NeoForge" && !string.IsNullOrEmpty(loaderVersion))
                {
                    string neoForgeJarPath = Path.Combine(createdInstancePath, "installer.jar");
                    await _neoForgeProvider.DownloadNeoForgeJarAsync(selectedVersion.Id, loaderVersion, neoForgeJarPath, progress);
                }
                else if (!isBedrock)
                {
                    await provider.DownloadSoftwareAsync(selectedVersion.Id, jarPath, progress);
                }


                if (ChkAcceptEula.IsChecked == true && createdFolderName != null)
                {
                    _instanceManager.AcceptEula(createdFolderName);
                }

                if (ChkEnableGeyser.IsChecked == true && createdInstancePath != null)
                {
                    try
                    {
                        TxtProgress.Text = "Setting up Geyser cross-play...";
                        await _geyserProvisioning.EnsureGeyserSetupAsync(createdInstancePath, serverType, selectedVersion.Id, progress);

                        // Persist the HasGeyser flag so the dashboard shows the Bedrock IP row
                        metadata.HasGeyser = true;
                        _instanceManager.SaveMetadata(metadata, createdInstancePath);
                    }
                    catch (InvalidOperationException geyserEx)
                    {
                        // Cross-play failed (version/loader not supported, API down, etc.)
                        // but the server itself is still valid — don't destroy the instance.
                        _logger.LogWarning(geyserEx, "Geyser provisioning failed for {ServerType} {McVersion}. Continuing without cross-play.", serverType, selectedVersion.Id);

                        metadata.HasGeyser = false;
                        _instanceManager.SaveMetadata(metadata, createdInstancePath);

                        // Clean up any partial Geyser/Floodgate jars that may have been downloaded
                        CleanupGeyserFiles(createdInstancePath);

                        Dispatcher.Invoke(() =>
                        {
                            PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(
                                "Cross-play Unavailable",
                                $"Cross-play Setup Failed\n\n{geyserEx.Message}\n\nYour server was created without cross-play. You can add it later from Server Settings.");
                        });
                    }
                }

                if (!NavigateToDashboard())
                {
                    SetCreationState(false);
                    _logger.LogWarning("Instance {InstanceName} was created, but PocketMC could not navigate back to the dashboard automatically.", TxtName.Text);
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowInfo(
                        "Instance Created",
                        "The instance was created successfully, but PocketMC could not return to the Dashboard automatically.");
                }
            }
            catch (Exception ex)
            {
                await CleanupFailedInstanceAsync(createdFolderName, createdInstancePath);
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

        private static void CleanupGeyserFiles(string instancePath)
        {
            // Remove any partial Geyser/Floodgate JARs from both possible target directories
            string[] targetDirs = { "plugins", "mods" };
            string[] geyserFiles = { "Geyser.jar", "Geyser.jar.partial", "Floodgate.jar", "Floodgate.jar.partial" };

            foreach (string dir in targetDirs)
            {
                string dirPath = Path.Combine(instancePath, dir);
                if (!Directory.Exists(dirPath)) continue;

                foreach (string fileName in geyserFiles)
                {
                    string filePath = Path.Combine(dirPath, fileName);
                    try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
                }
            }

            // Also remove the connect guide
            try
            {
                string guidePath = Path.Combine(instancePath, "BEDROCK-CONNECT.txt");
                if (File.Exists(guidePath)) File.Delete(guidePath);
            }
            catch { }
        }

        private async Task CleanupFailedInstanceAsync(string? folderName, string? instancePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    await _instanceManager.DeleteInstanceAsync(folderName);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(instancePath) && Directory.Exists(instancePath))
                {
                    await PocketMC.Desktop.Infrastructure.FileSystem.FileUtils.CleanDirectoryAsync(instancePath);
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



        private static string FormatMegabytes(long bytes)
        {
            double megabytes = bytes / 1024d / 1024d;
            return $"{megabytes:0.0} MB";
        }

        private IServerSoftwareProvider GetProvider(string serverType)
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

            if (string.Equals(serverType, "NeoForge", StringComparison.OrdinalIgnoreCase))
            {
                return _neoForgeProvider;
            }

            if (serverType.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase))
            {
                return _bedrockProvider;
            }

            if (serverType.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase))
            {
                return _pocketmineProvider;
            }

            return _vanillaProvider;
        }
    }
}
