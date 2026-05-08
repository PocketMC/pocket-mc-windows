using System;
using System.Windows.Input;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Infrastructure.Process;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsPerformanceVM : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly Action _markDirty;
        private readonly double _totalRamMb;

        private double _minRam = 1024;
        public double MinRam { get => _minRam; set { if (SetProperty(ref _minRam, value)) { _markDirty(); OnPropertyChanged(nameof(MinRamDisplay)); } } }
        public string MinRamDisplay => $"{MinRam:N0} MB";

        private double _maxRam = 4096;
        public double MaxRam { get => _maxRam; set { if (SetProperty(ref _maxRam, value)) { _markDirty(); OnPropertyChanged(nameof(MaxRamDisplay)); CheckRamWarning(); } } }
        public string MaxRamDisplay => $"{MaxRam:N0} MB";

        private bool _showRamWarning;
        public bool ShowRamWarning { get => _showRamWarning; set => SetProperty(ref _showRamWarning, value); }
        public double TotalRamMb => _totalRamMb;

        private string? _javaPath;
        public string? JavaPath { get => _javaPath; set { if (SetProperty(ref _javaPath, value)) _markDirty(); } }

        private string? _advancedJvmArgs;
        public string? AdvancedJvmArgs { get => _advancedJvmArgs; set { if (SetProperty(ref _advancedJvmArgs, value)) _markDirty(); } }

        public ICommand BrowseJavaCommand { get; }

        public SettingsPerformanceVM(IDialogService dialogService, Action markDirty)
        {
            _dialogService = dialogService;
            _markDirty = markDirty;
            _totalRamMb = MemoryHelper.GetTotalPhysicalMemoryMb();
            BrowseJavaCommand = new RelayCommand(async _ => await BrowseJavaAsync());
        }

        public async System.Threading.Tasks.Task BrowseJavaAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Java Executable", "java.exe|java.exe|Executables (*.exe)|*.exe");
            if (file != null) JavaPath = file;
        }

        private void CheckRamWarning() { if (_totalRamMb > 0) ShowRamWarning = MaxRam > (_totalRamMb * 0.8); }
    }
}
