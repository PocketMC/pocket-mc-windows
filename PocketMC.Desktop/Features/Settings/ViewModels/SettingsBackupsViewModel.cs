using PocketMC.Desktop.Features.Settings.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Domain.Models;
using PocketMC.Application.Instances.Backups;

namespace PocketMC.Desktop.Features.Settings.ViewModels
{
    public class SettingsBackupsViewModel : ViewModelBase
    {
        private readonly InstanceMetadata _metadata;
        private string _serverDir;

        public void UpdateServerDir(string newDir) => _serverDir = newDir;
        private readonly BackupService _backupService;
        private readonly IDialogService _dialogService;
        private readonly IAppDispatcher _dispatcher;
        private readonly Func<bool> _isRunningCheck;
        private readonly Action _markDirty;

        private int _backupIntervalHours = 24;
        public int BackupIntervalHours { get => _backupIntervalHours; set { if (SetProperty(ref _backupIntervalHours, value)) _markDirty(); } }

        private int _maxBackupsToKeep = 5;
        public int MaxBackupsToKeep { get => _maxBackupsToKeep; set { if (SetProperty(ref _maxBackupsToKeep, value)) _markDirty(); } }

        private string? _customBackupDirectory;
        public string? CustomBackupDirectory
        {
            get => _customBackupDirectory;
            set
            {
                if (SetProperty(ref _customBackupDirectory, value))
                {
                    _markDirty();
                    LoadBackups();
                }
            }
        }

        public ObservableCollection<BackupItemViewModel> BackupList { get; } = new();

        private bool _isBackingUp;
        public bool IsBackingUp { get => _isBackingUp; set => SetProperty(ref _isBackingUp, value); }

        // --- Health Warning Properties ---

        private bool _hasHealthWarning;
        public bool HasHealthWarning { get => _hasHealthWarning; set => SetProperty(ref _hasHealthWarning, value); }

        private string _healthWarningText = string.Empty;
        public string HealthWarningText { get => _healthWarningText; set => SetProperty(ref _healthWarningText, value); }

        private string _healthWarningIcon = "Warning24";
        public string HealthWarningIcon { get => _healthWarningIcon; set => SetProperty(ref _healthWarningIcon, value); }

        private bool _hasHealthCritical;
        public bool HasHealthCritical { get => _hasHealthCritical; set => SetProperty(ref _hasHealthCritical, value); }

        // --- Backup Stats ---

        private string _totalBackupsSizeText = string.Empty;
        public string TotalBackupsSizeText { get => _totalBackupsSizeText; set => SetProperty(ref _totalBackupsSizeText, value); }

        private string _lastBackupTimeText = "Never";
        public string LastBackupTimeText { get => _lastBackupTimeText; set => SetProperty(ref _lastBackupTimeText, value); }

        private string _diskSpaceText = string.Empty;
        public string DiskSpaceText { get => _diskSpaceText; set => SetProperty(ref _diskSpaceText, value); }

        private bool _showDiskWarning;
        public bool ShowDiskWarning { get => _showDiskWarning; set => SetProperty(ref _showDiskWarning, value); }

        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand SaveLabelCommand { get; }
        public ICommand VerifyIntegrityCommand { get; }
        public ICommand BrowseBackupDirCommand { get; }
        public ICommand ResetBackupDirCommand { get; }

        public SettingsBackupsViewModel(
            InstanceMetadata metadata,
            string serverDir,
            BackupService backupService,
            IDialogService dialogService,
            IAppDispatcher dispatcher,
            Func<bool> isRunningCheck,
            Action markDirty)
        {
            _metadata = metadata;
            _serverDir = serverDir;
            _backupService = backupService;
            _dialogService = dialogService;
            _dispatcher = dispatcher;
            _isRunningCheck = isRunningCheck;
            _markDirty = markDirty;

            _backupIntervalHours = metadata.BackupIntervalHours;
            _maxBackupsToKeep = metadata.MaxBackupsToKeep;
            _customBackupDirectory = metadata.CustomBackupDirectory;

            CreateBackupCommand = new RelayCommand(async _ => await CreateBackupAsync());
            RestoreBackupCommand = new RelayCommand(async p => await RestoreBackupAsync(p as string), _ => !_isRunningCheck());
            DeleteBackupCommand = new RelayCommand(async p => await DeleteBackupAsync(p as string));
            SaveLabelCommand = new RelayCommand(async p => await SaveLabelAsync(p as BackupItemViewModel));
            VerifyIntegrityCommand = new RelayCommand(async p => await VerifyIntegrityAsync(p as BackupItemViewModel));
            BrowseBackupDirCommand = new RelayCommand(async _ => await BrowseBackupDirAsync());
            ResetBackupDirCommand = new RelayCommand(_ => CustomBackupDirectory = null);
        }

        private string GetBackupDirectory()
        {
            return string.IsNullOrWhiteSpace(CustomBackupDirectory)
                ? Path.Combine(_serverDir, "backups")
                : CustomBackupDirectory;
        }

        private async Task BrowseBackupDirAsync()
        {
            var folder = await _dialogService.OpenFolderDialogAsync("Select Custom Backup Folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                CustomBackupDirectory = folder;
            }
        }

        public void LoadBackups()
        {
            BackupList.Clear();

            var defaultDir = Path.Combine(_serverDir, "backups");
            var customDir = CustomBackupDirectory;

            var directoriesToScan = new System.Collections.Generic.List<string> { defaultDir };
            if (!string.IsNullOrWhiteSpace(customDir) && customDir != defaultDir && Directory.Exists(customDir))
            {
                directoriesToScan.Add(customDir);
            }

            var zipFiles = new System.Collections.Generic.List<string>();
            foreach (var dir in directoriesToScan)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        zipFiles.AddRange(Directory.GetFiles(dir, "*.zip"));
                    }
                    catch
                    {
                        // Ignore scan errors for robust fallback
                    }
                }
            }

            var uniqueZips = zipFiles
                .Select(f => new FileInfo(f))
                .GroupBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(fi => !string.IsNullOrWhiteSpace(customDir) && fi.FullName.StartsWith(customDir, StringComparison.OrdinalIgnoreCase)).First())
                .OrderByDescending(fi => fi.CreationTime)
                .ToList();

            var manifest = BackupManifest.Load(_serverDir);

            foreach (var fi in uniqueZips)
            {
                var metaEntry = manifest.Entries.FirstOrDefault(e =>
                    string.Equals(e.FileName, fi.Name, StringComparison.OrdinalIgnoreCase));

                bool isDefault = fi.FullName.StartsWith(defaultDir, StringComparison.OrdinalIgnoreCase);

                var vm = new BackupItemViewModel
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    SizeMb = fi.Length / (1024.0 * 1024.0),
                    CreatedAt = fi.CreationTime,
                    LocationText = isDefault ? "Default" : "Custom"
                };

                if (metaEntry != null)
                {
                    vm.Version = metaEntry.Version;
                    vm.TriggerText = metaEntry.Trigger == BackupTrigger.Manual ? "Manual" : "Scheduled";
                    vm.Label = metaEntry.Label ?? string.Empty;
                    vm.ServerVersion = metaEntry.MinecraftVersion;
                    vm.ServerType = metaEntry.ServerType;
                    vm.HasChecksum = metaEntry.Sha256Checksum != null;
                    vm.IntegrityVerified = metaEntry.IntegrityVerified;

                    if (metaEntry.SizeDeltaBytes.HasValue)
                    {
                        double deltaMb = metaEntry.SizeDeltaBytes.Value / (1024.0 * 1024.0);
                        vm.SizeDeltaText = metaEntry.SizeDeltaBytes.Value >= 0
                            ? $"+{deltaMb:F1} MB"
                            : $"{deltaMb:F1} MB";
                    }
                }

                BackupList.Add(vm);
            }

            RefreshHealthWarnings();
        }

        private void RefreshHealthWarnings()
        {
            var warnings = new System.Collections.Generic.List<string>();
            bool critical = false;

            // 1. Check for recent backup failure
            var manifest = BackupManifest.Load(_serverDir);
            if (manifest.LastFailedBackupUtc.HasValue)
            {
                var ago = DateTime.UtcNow - manifest.LastFailedBackupUtc.Value;
                string timeAgo = ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes}m ago"
                    : ago.TotalDays < 1 ? $"{(int)ago.TotalHours}h ago"
                    : $"{(int)ago.TotalDays}d ago";
                warnings.Add($"Last backup failed ({timeAgo}): {manifest.LastFailureReason ?? "Unknown error"}");
                critical = true;
            }

            // 2. Check disk space
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(_serverDir) ?? "C");
                double freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                double totalGb = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                double usedPercent = ((totalGb - freeGb) / totalGb) * 100;

                DiskSpaceText = $"{freeGb:F1} GB free of {totalGb:F0} GB";
                ShowDiskWarning = freeGb < 2.0;

                if (freeGb < 1.0)
                {
                    warnings.Add($"Critical: Only {freeGb:F1} GB disk space remaining. Backups may fail.");
                    critical = true;
                }
                else if (freeGb < 2.0)
                {
                    warnings.Add($"Low disk space: {freeGb:F1} GB remaining. Consider freeing space.");
                }
            }
            catch { /* Ignore drive info errors */ }

            // 3. Check for overdue scheduled backup
            if (_metadata.BackupIntervalHours > 0 && _metadata.LastBackupTime.HasValue)
            {
                var nextDue = _metadata.LastBackupTime.Value.AddHours(_metadata.BackupIntervalHours);
                if (DateTime.UtcNow > nextDue.AddHours(1)) // 1 hour grace period
                {
                    var overdue = DateTime.UtcNow - nextDue;
                    string overdueText = overdue.TotalHours < 1 ? $"{(int)overdue.TotalMinutes}m"
                        : overdue.TotalDays < 1 ? $"{(int)overdue.TotalHours}h"
                        : $"{(int)overdue.TotalDays}d";
                    warnings.Add($"Scheduled backup is overdue by {overdueText}.");
                }
            }

            // 4. No backups exist warning
            if (BackupList.Count == 0 && _metadata.BackupIntervalHours > 0)
            {
                warnings.Add("No backups found. Consider creating your first backup now.");
            }

            // 5. Total size display
            double totalSizeMb = BackupList.Sum(b => b.SizeMb);
            TotalBackupsSizeText = totalSizeMb >= 1024
                ? $"{totalSizeMb / 1024.0:F1} GB across {BackupList.Count} backup(s)"
                : $"{totalSizeMb:F1} MB across {BackupList.Count} backup(s)";

            // 6. Last backup time
            if (_metadata.LastBackupTime.HasValue)
            {
                var ago = DateTime.UtcNow - _metadata.LastBackupTime.Value;
                if (ago.TotalMinutes < 1)
                    LastBackupTimeText = "Just now";
                else if (ago.TotalHours < 1)
                    LastBackupTimeText = $"{(int)ago.TotalMinutes} minutes ago";
                else if (ago.TotalDays < 1)
                    LastBackupTimeText = $"{(int)ago.TotalHours} hours ago";
                else
                    LastBackupTimeText = $"{(int)ago.TotalDays} days ago ({_metadata.LastBackupTime.Value.ToLocalTime():g})";
            }
            else
            {
                LastBackupTimeText = "Never";
            }

            HasHealthWarning = warnings.Count > 0;
            HasHealthCritical = critical;
            HealthWarningText = string.Join("\n", warnings);
        }

        private async Task CreateBackupAsync()
        {
            IsBackingUp = true;
            try
            {
                _metadata.CustomBackupDirectory = CustomBackupDirectory;
                await _backupService.RunBackupAsync(_metadata, _serverDir);
                LoadBackups();
            }
            catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            finally { IsBackingUp = false; }
        }

        private async Task RestoreBackupAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Restore Backup", "This will COMPLETELY OVERWRITE current server files. Continue?", DialogType.Warning) == DialogResult.Yes)
            {
                IsBackingUp = true;
                try { await _backupService.RestoreBackupAsync(_metadata, path, _serverDir); _dialogService.ShowMessage("Success", "Backup restored successfully."); }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
                finally { IsBackingUp = false; }
            }
        }

        private async Task DeleteBackupAsync(string? path)
        {
            if (path != null && await _dialogService.ShowDialogAsync("Confirm", "Delete this backup permanently?", DialogType.Question) == DialogResult.Yes)
            {
                try
                {
                    var fileName = Path.GetFileName(path);
                    File.Delete(path);

                    // Remove from manifest
                    var manifest = BackupManifest.Load(_serverDir);
                    manifest.Entries.RemoveAll(e =>
                        string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                    manifest.Save(_serverDir);

                    LoadBackups();
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }

        private async Task SaveLabelAsync(BackupItemViewModel? item)
        {
            if (item == null) return;
            try
            {
                await Task.Run(() =>
                {
                    var manifest = BackupManifest.Load(_serverDir);
                    var entry = manifest.Entries.FirstOrDefault(e =>
                        string.Equals(e.FileName, item.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        entry.Label = string.IsNullOrWhiteSpace(item.Label) ? null : item.Label.Trim();
                        manifest.Save(_serverDir);
                    }
                });
                _dialogService.ShowMessage("Saved", "Backup label updated.");
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", $"Failed to save label: {ex.Message}", DialogType.Error);
            }
        }

        private async Task VerifyIntegrityAsync(BackupItemViewModel? item)
        {
            if (item == null) return;
            try
            {
                item.IntegrityStatusText = "Verifying...";
                var result = await Task.Run(() => _backupService.VerifyBackupIntegrity(_serverDir, item.Name, item.FullPath));

                if (result == null)
                {
                    item.IntegrityStatusText = "⚠ No checksum available";
                    item.IntegrityVerified = false;
                }
                else if (result == true)
                {
                    item.IntegrityStatusText = "✓ Integrity verified";
                    item.IntegrityVerified = true;
                }
                else
                {
                    item.IntegrityStatusText = "✗ CORRUPT — checksum mismatch!";
                    item.IntegrityVerified = false;
                    _dialogService.ShowMessage("Integrity Check Failed",
                        $"Backup \"{item.Name}\" may be corrupted. The SHA-256 checksum does not match the original. This backup may not restore correctly.",
                        DialogType.Warning);
                }
            }
            catch (Exception ex)
            {
                item.IntegrityStatusText = $"Error: {ex.Message}";
            }
        }
    }

    public class BackupItemViewModel : ViewModelBase
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public double SizeMb { get; set; }
        public DateTime CreatedAt { get; set; }

        // Versioning fields
        public int Version { get; set; }
        public string TriggerText { get; set; } = "";
        public string ServerVersion { get; set; } = "";
        public string ServerType { get; set; } = "";
        public string SizeDeltaText { get; set; } = "";
        public bool HasChecksum { get; set; }

        private string _locationText = "Default";
        public string LocationText { get => _locationText; set => SetProperty(ref _locationText, value); }

        private bool _integrityVerified;
        public bool IntegrityVerified { get => _integrityVerified; set => SetProperty(ref _integrityVerified, value); }

        private string _label = string.Empty;
        public string Label { get => _label; set => SetProperty(ref _label, value); }

        private string _integrityStatusText = string.Empty;
        public string IntegrityStatusText { get => _integrityStatusText; set => SetProperty(ref _integrityStatusText, value); }

        /// <summary>Display-friendly version text, e.g. "v3"</summary>
        public string VersionText => Version > 0 ? $"v{Version}" : "";

        /// <summary>Formatted creation time for the UI.</summary>
        public string CreatedAtText => CreatedAt.ToString("MMM dd, yyyy  hh:mm tt");

        /// <summary>Formatted size for the UI.</summary>
        public string SizeText => SizeMb >= 1024 ? $"{SizeMb / 1024.0:F1} GB" : $"{SizeMb:F1} MB";

        /// <summary>Has metadata from manifest (vs legacy backup with no manifest).</summary>
        public bool HasMetadata => Version > 0;
    }
}
