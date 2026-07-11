using PocketMC.Infrastructure.Marketplace;
using System.Windows.Media;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Marketplace
{
    public class AddonInstallRowViewModel : ViewModelBase
    {
        private double _progressValue;
        private bool _isDownloading;
        private string _statusText = "Waiting...";
        private Brush _statusForeground = Brushes.Gray;

        public ResolvedDependency ResolvedItem { get; init; } = null!;

        public string ProjectTitle => ResolvedItem.ProjectTitle;
        public string FileName => ResolvedItem.FileName;

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set { SetProperty(ref _isDownloading, value); OnPropertyChanged(nameof(HasStatus)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { SetProperty(ref _statusText, value); OnPropertyChanged(nameof(HasStatus)); }
        }

        public Brush StatusForeground
        {
            get => _statusForeground;
            set => SetProperty(ref _statusForeground, value);
        }

        public bool HasStatus => !string.IsNullOrEmpty(StatusText) || IsDownloading;
    }
}
