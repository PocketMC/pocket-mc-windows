using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Features.Java;

namespace PocketMC.Desktop.Features.Setup
{
    /// <summary>
    /// View-model for a single Java runtime row in the management page.
    /// </summary>
    public class JavaRuntimeEntry : INotifyPropertyChanged
    {
        private bool _isInstalled;
        private bool _isCustom;
        private string? _path;
        private JavaProvisioningStage _stage;
        private bool _hasError;

        public int Version { get; set; }
        public string VersionLabel => Version > 0 ? $"{Version}" : "?";
        public string DisplayName { get; set; } = "";
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                _isInstalled = value;
                Refresh();
            }
        }

        public bool IsCustom
        {
            get => _isCustom;
            set
            {
                _isCustom = value;
                Refresh();
            }
        }

        public string? Path
        {
            get => _path;
            set
            {
                _path = value;
                OnPropertyChanged(nameof(Path));
            }
        }

        public JavaProvisioningStage Stage
        {
            get => _stage;
            set
            {
                _stage = value;
                Refresh();
            }
        }

        public bool HasError
        {
            get => _hasError;
            set
            {
                _hasError = value;
                Refresh();
            }
        }

        public bool IsProvisioning =>
            !IsCustom &&
            Stage is JavaProvisioningStage.Queued
                or JavaProvisioningStage.ResolvingPackage
                or JavaProvisioningStage.Downloading
                or JavaProvisioningStage.Extracting
                or JavaProvisioningStage.Verifying;

        // ── Badge (subtle semi-transparent fills) ──
        public string BadgeText => IsCustom
            ? "CUSTOM"
            : HasError
                ? "ERROR"
                : Stage switch
                {
                    JavaProvisioningStage.Queued or JavaProvisioningStage.ResolvingPackage => "PREPARING",
                    JavaProvisioningStage.Downloading => "DOWNLOADING",
                    JavaProvisioningStage.Extracting => "EXTRACTING",
                    JavaProvisioningStage.Verifying => "VERIFYING",
                    _ when IsInstalled => "READY",
                    _ => "MISSING"
                };
        public Visibility BadgeVisibility => Visibility.Visible;
        public SolidColorBrush BadgeBackground => IsCustom
            ? new SolidColorBrush(Color.FromArgb(0x30, 0xA0, 0x8C, 0xFF))   // soft violet tint
            : HasError
                ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x66, 0x66))
                : IsProvisioning
                    ? new SolidColorBrush(Color.FromArgb(0x30, 0x66, 0xCC, 0xFF))
                    : IsInstalled
                        ? new SolidColorBrush(Color.FromArgb(0x30, 0x60, 0xCD, 0xFF))
                        : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x99, 0x66));
        public SolidColorBrush BadgeForeground => IsCustom
            ? new SolidColorBrush(Color.FromRgb(0xC0, 0xB4, 0xFF))  // light violet
            : HasError
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C))
                : IsProvisioning
                    ? new SolidColorBrush(Color.FromRgb(0x8B, 0xD0, 0xFF))
                    : IsInstalled
                        ? new SolidColorBrush(Color.FromRgb(0x78, 0xB8, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x88));

        // ── Version tile (left icon) ──
        public SolidColorBrush StatusBackground => HasError
            ? new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0x66, 0x66))
            : IsProvisioning
                ? new SolidColorBrush(Color.FromArgb(0x25, 0x66, 0xCC, 0xFF))
                : IsInstalled
                    ? new SolidColorBrush(Color.FromArgb(0x25, 0x60, 0xCD, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));

        // ── Status icon (Segoe Fluent glyph) ──
        public string StatusIcon => HasError
            ? "\uEA39"
            : IsProvisioning
                ? "\uE895"
                : IsInstalled
                    ? "\uE73E"
                    : "\uE896";
        public SolidColorBrush StatusIconForeground => HasError
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C))
            : IsProvisioning
                ? new SolidColorBrush(Color.FromRgb(0x8B, 0xD0, 0xFF))
                : IsInstalled
                    ? new SolidColorBrush(Color.FromRgb(0x78, 0xB8, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

        // ── Detail line ──
        private string _detailText = "";
        public string DetailText
        {
            get => _detailText;
            set { _detailText = value; OnPropertyChanged(nameof(DetailText)); }
        }

        // ── Progress (download) ──
        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        private Visibility _progressVisibility = Visibility.Collapsed;
        public Visibility ProgressVisibility
        {
            get => _progressVisibility;
            set { _progressVisibility = value; OnPropertyChanged(nameof(ProgressVisibility)); }
        }

        // ── Delete button ──
        public Visibility DeleteVisibility => IsInstalled && !IsProvisioning ? Visibility.Visible : Visibility.Collapsed;

        // ── Download button ──
        public Visibility DownloadVisibility => !IsInstalled && !IsCustom && !IsProvisioning ? Visibility.Visible : Visibility.Collapsed;

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(Stage));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsProvisioning));
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(BadgeBackground));
            OnPropertyChanged(nameof(BadgeForeground));
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusIconForeground));
            OnPropertyChanged(nameof(StatusBackground));
            OnPropertyChanged(nameof(DeleteVisibility));
            OnPropertyChanged(nameof(DownloadVisibility));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class JavaSetupPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly JavaProvisioningService _javaProvisioning;
        private readonly ILogger<JavaSetupPage> _logger;
        private readonly PocketMC.Desktop.Features.Settings.SettingsManager _settingsManager;
        private readonly InstanceRegistry _instanceRegistry;
        private readonly ServerProcessManager _processManager;
        private bool _isSubscribedToProvisioning;
        public ObservableCollection<JavaRuntimeEntry> Runtimes { get; } = new();

        public JavaSetupPage(
            ApplicationState applicationState,
            JavaProvisioningService javaProvisioning,
            PocketMC.Desktop.Features.Settings.SettingsManager settingsManager,
            InstanceRegistry instanceRegistry,
            ServerProcessManager processManager,
            ILogger<JavaSetupPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _javaProvisioning = javaProvisioning;
            _settingsManager = settingsManager;
            _instanceRegistry = instanceRegistry;
            _processManager = processManager;
            _logger = logger;
            RuntimeList.ItemsSource = Runtimes;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SubscribeToProvisioning();
            ScanRuntimes();
            ApplyProvisioningStatuses();
            _javaProvisioning.StartBackgroundProvisioning();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromProvisioning();
        }

        /// <summary>
        /// Scans the runtime directory and builds the card list.
        /// Uses JavaProvisioningService.IsJavaVersionPresent for integrity checks.
        /// </summary>
        private void ScanRuntimes()
        {
            Runtimes.Clear();
            string appRoot = _applicationState.GetRequiredAppRootPath();
            var requiredVersions = JavaRuntimeResolver.GetBundledJavaVersions();

            foreach (var version in requiredVersions)
            {
                string runtimeDir = System.IO.Path.Combine(appRoot, "runtime", $"java{version}");
                bool installed = _javaProvisioning.IsJavaVersionPresent(version);

                string detail;
                if (installed)
                {
                    double sizeMb = GetDirectorySizeMb(runtimeDir);
                    detail = $"{runtimeDir}  •  {sizeMb:F1} MB";
                }
                else
                {
                    detail = "Missing runtime. PocketMC will download it automatically.";
                }

                string mcRange = version switch
                {
                    8 => "MC 1.0 – 1.16.4",
                    11 => "MC 1.16.5 – 1.17.1",
                    17 => "MC 1.18 – 1.20.4",
                    21 => "MC 1.20.5 – 1.21.1",
                    25 => "MC 1.21.2+",
                    _ => ""
                };

                Runtimes.Add(new JavaRuntimeEntry
                {
                    Version = version,
                    DisplayName = $"Java {version} Runtime  ({mcRange})",
                    IsInstalled = installed,
                    IsCustom = false,
                    Path = runtimeDir,
                    DetailText = detail
                });
            }

            // Scan for custom runtimes (folders not matching bundled versions)
            string runtimeRoot = System.IO.Path.Combine(appRoot, "runtime");
            if (Directory.Exists(runtimeRoot))
            {
                foreach (var dir in Directory.GetDirectories(runtimeRoot))
                {
                    string folderName = System.IO.Path.GetFileName(dir);
                    // Skip known bundled folders
                    if (requiredVersions.Any(v => folderName == $"java{v}"))
                        continue;

                    string javaExe = System.IO.Path.Combine(dir, "bin", "java.exe");
                    bool exists = File.Exists(javaExe);

                    Runtimes.Add(new JavaRuntimeEntry
                    {
                        Version = 0,
                        DisplayName = folderName,
                        IsInstalled = exists,
                        IsCustom = true,
                        Path = dir,
                        DetailText = exists
                            ? $"{dir}  •  {GetDirectorySizeMb(dir):F1} MB"
                            : $"{dir}  •  java.exe not found"
                    });
                }
            }

            int installedCount = Runtimes.Count(r => r.IsInstalled);
            int total = Runtimes.Count;
            TxtGlobalStatus.Text = $"{installedCount} of {total} runtimes installed";
            TxtGlobalStatus.Foreground = Brushes.Silver;
        }

        // ──────────────────────────────────────────────
        //  Download Missing
        // ──────────────────────────────────────────────

        private async void BtnDownloadMissing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnDownloadMissing.IsEnabled = false;
                TxtGlobalStatus.Text = "Checking and repairing missing runtimes...";
                TxtGlobalStatus.Foreground = Brushes.Silver;
                await _javaProvisioning.EnsureBundledRuntimesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Manual runtime provisioning did not complete successfully.");
                TxtGlobalStatus.Text = ex.Message;
                TxtGlobalStatus.Foreground = Brushes.OrangeRed;
            }
            finally
            {
                UpdateGlobalStatus();
            }
        }

        // ──────────────────────────────────────────────
        //  Add Custom Runtime
        // ──────────────────────────────────────────────

        private void BtnAddCustom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select a Java runtime folder (must contain bin/java.exe)"
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName;
                string javaExe = System.IO.Path.Combine(selectedPath, "bin", "java.exe");

                if (!File.Exists(javaExe))
                {
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(
                        "Invalid Runtime",
                        "Selected folder does not contain bin/java.exe.\nPlease select the JRE/JDK root folder.");
                    return;
                }

                // Copy to runtime directory with a custom name
                string appRoot = _applicationState.GetRequiredAppRootPath();
                string folderName = System.IO.Path.GetFileName(selectedPath);
                string destPath = System.IO.Path.Combine(appRoot, "runtime", $"custom-{folderName}");

                try
                {
                    if (Directory.Exists(destPath))
                    {
                        PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(
                            "Duplicate",
                            $"A runtime named 'custom-{folderName}' already exists.");
                        return;
                    }

                    CopyDirectory(selectedPath, destPath);
                    ScanRuntimes();
                    TxtGlobalStatus.Text = $"Added custom runtime: {folderName}";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add custom runtime.");
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowError(
                        "Error",
                        $"Failed to add runtime: {ex.Message}");
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Delete Runtime
        // ──────────────────────────────────────────────

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is JavaRuntimeEntry entry && entry.Path != null)
            {
                var runningInstances = _processManager.ActiveProcesses.Keys
                    .Select(id => _instanceRegistry.GetById(id))
                    .Where(m => m != null);

                bool isUsedByRunningServer = false;
                foreach (var meta in runningInstances)
                {
                    string javaPath = JavaRuntimeResolver.ResolveJavaPath(meta!, _applicationState.GetRequiredAppRootPath());
                    if (!entry.IsCustom && JavaRuntimeResolver.IsBundledJavaPath(javaPath, entry.Version, _applicationState.GetRequiredAppRootPath()))
                    {
                        isUsedByRunningServer = true;
                        break;
                    }
                    if (entry.IsCustom && javaPath.StartsWith(entry.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        isUsedByRunningServer = true;
                        break;
                    }
                }

                if (isUsedByRunningServer)
                {
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(
                        "Cannot Delete",
                        $"Cannot delete {entry.DisplayName} because it is currently in use by a running server.");
                    return;
                }

                bool confirmed = PocketMC.Desktop.Infrastructure.AppDialog.Confirm(
                    "Confirm Delete",
                    $"Delete {entry.DisplayName}?\n\nPath: {entry.Path}\n\nYou can re-download bundled runtimes at any time.");

                if (confirmed)
                {
                    try
                    {
                        if (!entry.IsCustom)
                        {
                            var settings = _settingsManager.Load();
                            settings.UserRemovedJavaVersions.Add(entry.Version);
                            _settingsManager.Save(settings);
                        }

                        if (Directory.Exists(entry.Path))
                            Directory.Delete(entry.Path, true);

                        ScanRuntimes();
                        TxtGlobalStatus.Text = $"Deleted {entry.DisplayName}";
                    }
                    catch (Exception ex)
                    {
                        if (!entry.IsCustom)
                        {
                            var settings = _settingsManager.Load();
                            settings.UserRemovedJavaVersions.Remove(entry.Version);
                            _settingsManager.Save(settings);
                        }

                        _logger.LogError(ex, "Failed to delete runtime at {Path}.", entry.Path);
                        PocketMC.Desktop.Infrastructure.AppDialog.ShowError(
                            "Error",
                            $"Failed to delete: {ex.Message}");
                    }
                }
            }
        }
        // ──────────────────────────────────────────────
        //  Download Single
        // ──────────────────────────────────────────────

        private async void BtnDownloadSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is JavaRuntimeEntry entry && !entry.IsCustom)
            {
                try
                {
                    await _javaProvisioning.EnsureJavaAsync(entry.Version, isManualUserTriggered: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download single runtime.");
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowError(
                        "Error",
                        $"Failed to start download: {ex.Message}");
                }
                finally
                {
                    UpdateGlobalStatus();
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Refresh
        // ──────────────────────────────────────────────

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ScanRuntimes();
            ApplyProvisioningStatuses();
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private void SubscribeToProvisioning()
        {
            if (_isSubscribedToProvisioning)
            {
                return;
            }

            _javaProvisioning.OnProvisioningStatusChanged += OnProvisioningStatusChanged;
            _isSubscribedToProvisioning = true;
        }

        private void UnsubscribeFromProvisioning()
        {
            if (!_isSubscribedToProvisioning)
            {
                return;
            }

            _javaProvisioning.OnProvisioningStatusChanged -= OnProvisioningStatusChanged;
            _isSubscribedToProvisioning = false;
        }

        private void OnProvisioningStatusChanged(JavaProvisioningStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                var entry = Runtimes.FirstOrDefault(runtime => runtime.Version == status.Version && !runtime.IsCustom);
                if (entry == null)
                {
                    ScanRuntimes();
                    entry = Runtimes.FirstOrDefault(runtime => runtime.Version == status.Version && !runtime.IsCustom);
                }

                if (entry != null)
                {
                    ApplyProvisioningStatus(entry, status);
                }

                UpdateGlobalStatus();
            });
        }

        private void ApplyProvisioningStatuses()
        {
            foreach (var entry in Runtimes.Where(runtime => !runtime.IsCustom))
            {
                ApplyProvisioningStatus(entry, _javaProvisioning.GetStatus(entry.Version));
            }

            UpdateGlobalStatus();
        }

        private void ApplyProvisioningStatus(JavaRuntimeEntry entry, JavaProvisioningStatus status)
        {
            entry.Stage = status.Stage;
            entry.HasError = status.HasError;
            entry.IsInstalled = status.IsInstalled;
            entry.Progress = status.ProgressPercentage;
            entry.ProgressVisibility = status.IsBusy ? Visibility.Visible : Visibility.Collapsed;

            if (status.Stage == JavaProvisioningStage.Ready && status.IsInstalled)
            {
                entry.Path = Path.Combine(_applicationState.GetRequiredAppRootPath(), "runtime", $"java{entry.Version}");
                entry.DetailText = $"{entry.Path}  •  {GetDirectorySizeMb(entry.Path):F1} MB";
            }
            else if (status.HasError)
            {
                entry.DetailText = status.Message;
            }
            else if (status.IsBusy)
            {
                entry.DetailText = status.Message;
            }
            else if (!entry.IsInstalled)
            {
                entry.DetailText = "Missing runtime. PocketMC will download it automatically.";
            }
            else if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                entry.DetailText = $"{entry.Path}  •  {GetDirectorySizeMb(entry.Path):F1} MB";
            }

            entry.Refresh();
        }

        private void UpdateGlobalStatus()
        {
            var bundledEntries = Runtimes.Where(entry => !entry.IsCustom).ToList();
            var busyEntries = bundledEntries.Where(entry => entry.IsProvisioning).ToList();
            var failedEntries = bundledEntries.Where(entry => entry.HasError).ToList();
            int installedCount = bundledEntries.Count(entry => entry.IsInstalled);
            int total = bundledEntries.Count;

            BtnDownloadMissing.IsEnabled = busyEntries.Count == 0;

            if (busyEntries.Count > 0)
            {
                string active = busyEntries[0].DisplayName;
                TxtGlobalStatus.Text = busyEntries.Count == 1
                    ? $"{active}: {busyEntries[0].DetailText}"
                    : $"{active} and {busyEntries.Count - 1} more runtime(s) are being prepared...";
                TxtGlobalStatus.Foreground = Brushes.Silver;
                return;
            }

            if (failedEntries.Count > 0)
            {
                TxtGlobalStatus.Text = failedEntries[0].DetailText;
                TxtGlobalStatus.Foreground = Brushes.OrangeRed;
                return;
            }

            TxtGlobalStatus.Text = $"{installedCount} of {total} bundled runtimes installed";
            TxtGlobalStatus.Foreground = Brushes.Silver;
        }

        private static double GetDirectorySizeMb(string path)
        {
            if (!Directory.Exists(path)) return 0;
            try
            {
                long bytes = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                return bytes / (1024.0 * 1024.0);
            }
            catch { return 0; }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
    }
}
