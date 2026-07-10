using System.Windows.Media;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Features.Marketplace.Models
{
    public class ModDownloadTaskViewModel : ViewModelBase
    {
        public ModpackFile? Mod { get; set; }
        
        public bool IsCoreItem { get; set; } // e.g. Server Jar

        private string _projectTitle = "";
        public string ProjectTitle { get => _projectTitle; set => SetProperty(ref _projectTitle, value); }

        private string _fileName = "";
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

        private double _progressValue;
        public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        private string _statusText = "Waiting...";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private Brush _statusForeground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
        public Brush StatusForeground 
        { 
            get => _statusForeground; 
            set 
            {
                if (value is System.Windows.Freezable f && f.CanFreeze && !f.IsFrozen)
                {
                    f.Freeze();
                }
                SetProperty(ref _statusForeground, value); 
            } 
        }

        private bool _isDownloading;
        public bool IsDownloading { get => _isDownloading; set => SetProperty(ref _isDownloading, value); }
    }
}
