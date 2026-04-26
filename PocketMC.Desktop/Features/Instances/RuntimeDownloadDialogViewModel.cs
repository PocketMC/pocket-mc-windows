using System;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Instances.Services;

namespace PocketMC.Desktop.Features.Instances
{
    public class RuntimeDownloadDialogViewModel : ViewModelBase, IProgress<DownloadProgress>
    {
        private string _title = "Downloading Runtime";
        private string _statusMessage = "Preparing...";
        private double _progressPercentage;
        private bool _isComplete;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        public bool IsComplete
        {
            get => _isComplete;
            private set 
            { 
                if (SetProperty(ref _isComplete, value) && value)
                {
                    OnComplete?.Invoke();
                }
            }
        }

        public event Action? OnComplete;

        public void Report(DownloadProgress value)
        {
            ProgressPercentage = value.Percentage;
            StatusMessage = $"Downloading... {FormatSize(value.BytesRead)} / {FormatSize(value.TotalBytes)}";
        }

        public void Complete()
        {
            IsComplete = true;
        }

        private string FormatSize(long bytes) => bytes < 1048576 ? $"{bytes / 1024.0:F1} KB" : $"{bytes / 1048576.0:F1} MB";
    }
}
