using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances.Backups;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsBackupsVM : ViewModelBase
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

        public ObservableCollection<BackupItemViewModel> BackupList { get; } = new();

        private bool _isBackingUp;
        public bool IsBackingUp { get => _isBackingUp; set => SetProperty(ref _isBackingUp, value); }

        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand DeleteBackupCommand { get; }

        public SettingsBackupsVM(
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

            CreateBackupCommand = new RelayCommand(async _ => await CreateBackupAsync());
            RestoreBackupCommand = new RelayCommand(async p => await RestoreBackupAsync(p as string), _ => !_isRunningCheck());
            DeleteBackupCommand = new RelayCommand(async p => await DeleteBackupAsync(p as string));
        }

        public void LoadBackups()
        {
            BackupList.Clear();
            var dir = Path.Combine(_serverDir, "backups");
            if (!Directory.Exists(dir)) return;
            foreach (var zip in Directory.GetFiles(dir, "*.zip").OrderByDescending(f => File.GetCreationTime(f)))
            {
                var fi = new FileInfo(zip);
                BackupList.Add(new BackupItemViewModel { Name = fi.Name, FullPath = zip, SizeMb = fi.Length / (1024.0 * 1024.0), CreatedAt = fi.CreationTime });
            }
        }

        private async Task CreateBackupAsync()
        {
            IsBackingUp = true;
            try { await _backupService.RunBackupAsync(_metadata, _serverDir); LoadBackups(); }
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
                try { File.Delete(path); LoadBackups(); }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }
    }

    public class BackupItemViewModel
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public double SizeMb { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
