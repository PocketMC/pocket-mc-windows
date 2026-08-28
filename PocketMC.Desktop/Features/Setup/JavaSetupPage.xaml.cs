using PocketMC.Infrastructure.Configuration;
using PocketMC.Desktop.Infrastructure;
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
using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Players;
using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Infrastructure.Java;
using PocketMC.Infrastructure.Php;
using PocketMC.Infrastructure;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.OS;

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
                    : "";
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

    /// <summary>
    /// View-model for a single PHP runtime row in the management page.
    /// </summary>
    public class PhpRuntimeEntry : INotifyPropertyChanged
    {
        private bool _isInstalled;
        private string? _path;
        private PhpProvisioningStage _stage;
        private bool _hasError;

        public string Version { get; set; } = "";
        public string VersionLabel => $"{Version}";
        public string DisplayName { get; set; } = "";
        public string TargetPocketMineVersion { get; set; } = "";
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                _isInstalled = value;
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

        public PhpProvisioningStage Stage
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
            Stage is PhpProvisioningStage.Queued
                or PhpProvisioningStage.ResolvingPackage
                or PhpProvisioningStage.Downloading
                or PhpProvisioningStage.Extracting
                or PhpProvisioningStage.Verifying;

        public string BadgeText => HasError
            ? "ERROR"
            : Stage switch
            {
                PhpProvisioningStage.Queued or PhpProvisioningStage.ResolvingPackage => "PREPARING",
                PhpProvisioningStage.Downloading => "DOWNLOADING",
                PhpProvisioningStage.Extracting => "EXTRACTING",
                PhpProvisioningStage.Verifying => "VERIFYING",
                _ when IsInstalled => "READY",
                _ => "MISSING"
            };

        public Visibility BadgeVisibility => Visibility.Visible;
        public SolidColorBrush BadgeBackground => HasError
            ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x66, 0x66))
            : IsProvisioning
                ? new SolidColorBrush(Color.FromArgb(0x30, 0x66, 0xCC, 0xFF))
                : IsInstalled
                    ? new SolidColorBrush(Color.FromArgb(0x30, 0x60, 0xCD, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x99, 0x66));
        public SolidColorBrush BadgeForeground => HasError
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C))
            : IsProvisioning
                ? new SolidColorBrush(Color.FromRgb(0x8B, 0xD0, 0xFF))
                : IsInstalled
                    ? new SolidColorBrush(Color.FromRgb(0x78, 0xB8, 0xFF))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xBB, 0x88));

        public SolidColorBrush StatusBackground => HasError
            ? new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0x66, 0x66))
            : IsProvisioning
                ? new SolidColorBrush(Color.FromArgb(0x25, 0x66, 0xCC, 0xFF))
                : IsInstalled
                    ? new SolidColorBrush(Color.FromArgb(0x25, 0x60, 0xCD, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));

        public string StatusIcon => HasError
            ? "\uEA39"
            : IsProvisioning
                ? "\uE895"
                : IsInstalled
                    ? "\uE73E"
                    : "";
        public SolidColorBrush StatusIconForeground => HasError
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C))
            : IsProvisioning
                ? new SolidColorBrush(Color.FromRgb(0x8B, 0xD0, 0xFF))
                : IsInstalled
                    ? new SolidColorBrush(Color.FromRgb(0x78, 0xB8, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

        private string _detailText = "";
        public string DetailText
        {
            get => _detailText;
            set { _detailText = value; OnPropertyChanged(nameof(DetailText)); }
        }

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

        public Visibility DeleteVisibility => IsInstalled && !IsProvisioning ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DownloadVisibility => !IsInstalled && !IsProvisioning ? Visibility.Visible : Visibility.Collapsed;

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsInstalled));
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
        private readonly PhpProvisioningService _phpProvisioning;
        private readonly ILogger<JavaSetupPage> _logger;
        private readonly PocketMC.Infrastructure.Configuration.SettingsManager _settingsManager;
        private readonly InstanceRegistry _instanceRegistry;
        private readonly ServerProcessManager _processManager;
        private bool _isSubscribedToProvisioning;

        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(JavaSetupPage), new PropertyMetadata(true));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public ObservableCollection<JavaRuntimeEntry> Runtimes { get; } = new();
        public ObservableCollection<PhpRuntimeEntry> PhpRuntimes { get; } = new();

        public JavaSetupPage(
            ApplicationState applicationState,
            JavaProvisioningService javaProvisioning,
            PhpProvisioningService phpProvisioning,
            PocketMC.Infrastructure.Configuration.SettingsManager settingsManager,
            InstanceRegistry instanceRegistry,
            ServerProcessManager processManager,
            ILogger<JavaSetupPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _javaProvisioning = javaProvisioning;
            _phpProvisioning = phpProvisioning;
            _settingsManager = settingsManager;
            _instanceRegistry = instanceRegistry;
            _processManager = processManager;
            _logger = logger;

            RuntimeList.ItemsSource = Runtimes;
            PhpRuntimeList.ItemsSource = PhpRuntimes;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.EnableMouseWheelScrolling(this, RuntimeScrollViewer);
            SubscribeToProvisioning();
            IsLoading = true;
            await Task.Run(() => ScanRuntimes());
            ApplyProvisioningStatuses();
            IsLoading = false;
            _javaProvisioning.StartBackgroundProvisioning();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ScrollViewerHelper.DisableMouseWheelScrolling(this);
            UnsubscribeFromProvisioning();
        }

        /// <summary>
        /// Scans the runtime directory and builds the card list for Java and PHP.
        /// </summary>
        private void ScanRuntimes()
        {
            var javaList = new System.Collections.Generic.List<JavaRuntimeEntry>();
            var phpList = new System.Collections.Generic.List<PhpRuntimeEntry>();

            string appRoot = _applicationState.GetRequiredAppRootPath();

            // ── Scan Java Runtimes ──
            var requiredVersions = JavaRuntimeResolver.GetBundledJavaVersions().OrderByDescending(v => v).ToList();

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

                javaList.Add(new JavaRuntimeEntry
                {
                    Version = version,
                    DisplayName = $"Java {version} Runtime  ({mcRange})",
                    IsInstalled = installed,
                    IsCustom = false,
                    Path = runtimeDir,
                    DetailText = detail
                });
            }

            // Scan for custom Java runtimes
            string runtimeRoot = System.IO.Path.Combine(appRoot, "runtime");
            if (Directory.Exists(runtimeRoot))
            {
                foreach (var dir in Directory.GetDirectories(runtimeRoot))
                {
                    string folderName = System.IO.Path.GetFileName(dir);
                    if (requiredVersions.Any(v => folderName == $"java{v}") || folderName.StartsWith("php", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string javaExe = System.IO.Path.Combine(dir, "bin", "java.exe");
                    bool exists = File.Exists(javaExe);

                    javaList.Add(new JavaRuntimeEntry
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

            // ── Scan PHP Runtimes ──
            foreach (var def in PhpRuntimeResolver.GetReleaseDefinitions())
            {
                string phpDir = System.IO.Path.Combine(appRoot, "runtime", $"php{def.Version}");
                bool installed = _phpProvisioning.IsPhpVersionPresent(def.Version);

                string detail;
                if (installed)
                {
                    double sizeMb = GetDirectorySizeMb(phpDir);
                    detail = $"{phpDir}  •  {sizeMb:F1} MB";
                }
                else
                {
                    detail = $"Target: {def.TargetPocketMineVersion}. Missing runtime.";
                }

                phpList.Add(new PhpRuntimeEntry
                {
                    Version = def.Version,
                    DisplayName = def.DisplayName,
                    TargetPocketMineVersion = def.TargetPocketMineVersion,
                    IsInstalled = installed,
                    Path = phpDir,
                    DetailText = detail
                });
            }

            Dispatcher.Invoke(() =>
            {
                Runtimes.Clear();
                foreach (var item in javaList) Runtimes.Add(item);

                PhpRuntimes.Clear();
                foreach (var item in phpList) PhpRuntimes.Add(item);

                UpdateGlobalStatus();
            });
        }

        // ──────────────────────────────────────────────
        //  Download Missing (Java + PHP)
        // ──────────────────────────────────────────────

        private async void BtnDownloadMissing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnDownloadMissing.IsEnabled = false;
                TxtGlobalStatus.Text = "Checking and downloading missing runtimes...";
                TxtGlobalStatus.Foreground = Brushes.Silver;

                var javaTask = _javaProvisioning.EnsureBundledRuntimesAsync();
                var phpTask = _phpProvisioning.EnsureBundledRuntimesAsync();

                await Task.WhenAll(javaTask, phpTask);
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
        //  Add Custom Java Runtime
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
        //  Delete Java Runtime
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
        //  Delete PHP Runtime
        // ──────────────────────────────────────────────

        private async void BtnDeletePhp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PhpRuntimeEntry entry && entry.Path != null)
            {
                var runningInstances = _processManager.ActiveProcesses.Keys
                    .Select(id => _instanceRegistry.GetById(id))
                    .Where(m => m != null && CommandFormatter.IsPocketMine(m.ServerType));

                bool isUsedByRunningServer = false;
                foreach (var meta in runningInstances)
                {
                    string reqPhp = PhpRuntimeResolver.GetRequiredPhpVersion(meta!);
                    if (string.Equals(reqPhp, entry.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        isUsedByRunningServer = true;
                        break;
                    }
                }

                if (isUsedByRunningServer)
                {
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning(
                        "Cannot Delete",
                        $"Cannot delete {entry.DisplayName} because it is currently in use by a running PocketMine server.");
                    return;
                }

                bool confirmed = PocketMC.Desktop.Infrastructure.AppDialog.Confirm(
                    "Confirm Delete",
                    $"Delete {entry.DisplayName}?\n\nPath: {entry.Path}\n\nYou can re-download it at any time.");

                if (confirmed)
                {
                    try
                    {
                        await _phpProvisioning.DeletePhpVersionAsync(entry.Version);
                        ScanRuntimes();
                        ApplyProvisioningStatuses();
                        TxtGlobalStatus.Text = $"Deleted {entry.DisplayName}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete PHP runtime at {Path}.", entry.Path);
                        PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Error", $"Failed to delete PHP runtime: {ex.Message}");
                    }
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Download Single Java
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
        //  Download Single PHP
        // ──────────────────────────────────────────────

        private async void BtnDownloadPhpSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PhpRuntimeEntry entry)
            {
                try
                {
                    await _phpProvisioning.EnsurePhpVersionAsync(entry.Version);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download single PHP runtime.");
                    PocketMC.Desktop.Infrastructure.AppDialog.ShowError(
                        "Error",
                        $"Failed to download PHP {entry.Version}: {ex.Message}");
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
        //  Helpers & Subscriptions
        // ──────────────────────────────────────────────

        private void SubscribeToProvisioning()
        {
            if (_isSubscribedToProvisioning)
            {
                return;
            }

            _javaProvisioning.OnProvisioningStatusChanged += OnProvisioningStatusChanged;
            _phpProvisioning.OnProvisioningStatusChanged += OnPhpProvisioningStatusChanged;
            _isSubscribedToProvisioning = true;
        }

        private void UnsubscribeFromProvisioning()
        {
            if (!_isSubscribedToProvisioning)
            {
                return;
            }

            _javaProvisioning.OnProvisioningStatusChanged -= OnProvisioningStatusChanged;
            _phpProvisioning.OnProvisioningStatusChanged -= OnPhpProvisioningStatusChanged;
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

        private void OnPhpProvisioningStatusChanged(PhpProvisioningStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                var entry = PhpRuntimes.FirstOrDefault(runtime => string.Equals(runtime.Version, status.Version, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    ScanRuntimes();
                    entry = PhpRuntimes.FirstOrDefault(runtime => string.Equals(runtime.Version, status.Version, StringComparison.OrdinalIgnoreCase));
                }

                if (entry != null)
                {
                    ApplyPhpProvisioningStatus(entry, status);
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

            foreach (var entry in PhpRuntimes)
            {
                ApplyPhpProvisioningStatus(entry, _phpProvisioning.GetStatus(entry.Version));
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

        private void ApplyPhpProvisioningStatus(PhpRuntimeEntry entry, PhpProvisioningStatus status)
        {
            entry.Stage = status.Stage;
            entry.HasError = status.HasError;
            entry.IsInstalled = status.IsInstalled;
            entry.Progress = status.ProgressPercentage;
            entry.ProgressVisibility = status.IsBusy ? Visibility.Visible : Visibility.Collapsed;

            string phpDir = Path.Combine(_applicationState.GetRequiredAppRootPath(), "runtime", $"php{entry.Version}");

            if (status.Stage == PhpProvisioningStage.Ready && status.IsInstalled)
            {
                entry.Path = phpDir;
                entry.DetailText = $"{phpDir}  •  {GetDirectorySizeMb(phpDir):F1} MB";
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
                entry.DetailText = $"Target: {entry.TargetPocketMineVersion}. Missing runtime.";
            }
            else if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                entry.DetailText = $"{phpDir}  •  {GetDirectorySizeMb(phpDir):F1} MB";
            }

            entry.Refresh();
        }

        private void UpdateGlobalStatus()
        {
            var bundledJava = Runtimes.Where(entry => !entry.IsCustom).ToList();
            var busyJava = bundledJava.Where(entry => entry.IsProvisioning).ToList();
            var failedJava = bundledJava.Where(entry => entry.HasError).ToList();

            var busyPhp = PhpRuntimes.Where(entry => entry.IsProvisioning).ToList();
            var failedPhp = PhpRuntimes.Where(entry => entry.HasError).ToList();

            int installedJava = bundledJava.Count(entry => entry.IsInstalled);
            int installedPhp = PhpRuntimes.Count(entry => entry.IsInstalled);
            int totalJava = bundledJava.Count;
            int totalPhp = PhpRuntimes.Count;

            BtnDownloadMissing.IsEnabled = busyJava.Count == 0 && busyPhp.Count == 0;

            if (busyJava.Count > 0 || busyPhp.Count > 0)
            {
                string active = busyJava.Count > 0 ? busyJava[0].DisplayName : busyPhp[0].DisplayName;
                int remaining = busyJava.Count + busyPhp.Count - 1;
                TxtGlobalStatus.Text = remaining == 0
                    ? $"{active} is downloading..."
                    : $"{active} and {remaining} more runtime(s) are downloading...";
                TxtGlobalStatus.Foreground = Brushes.Silver;
                return;
            }

            if (failedJava.Count > 0 || failedPhp.Count > 0)
            {
                TxtGlobalStatus.Text = failedJava.Count > 0 ? failedJava[0].DetailText : failedPhp[0].DetailText;
                TxtGlobalStatus.Foreground = Brushes.OrangeRed;
                return;
            }

            TxtGlobalStatus.Text = $"{installedJava}/{totalJava} Java runtimes, {installedPhp}/{totalPhp} PHP runtimes installed";
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
