using System;
using System.Windows.Input;
using System.IO;
using System.Windows.Media.Imaging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsGeneralVM : ViewModelBase
    {
        private readonly string _serverDir;
        private readonly IDialogService _dialogService;
        private readonly IAppNavigationService _navigationService;
        private readonly Action _markDirty;

        private string? _motd;
        public string? Motd { get => _motd; set { if (SetProperty(ref _motd, value)) _markDirty(); } }

        private string _serverPort = "25565";
        public string ServerPort { get => _serverPort; set { if (SetProperty(ref _serverPort, value)) _markDirty(); } }

        private string? _serverIp;
        public string? ServerIp { get => _serverIp; set { if (SetProperty(ref _serverIp, value)) _markDirty(); } }

        private BitmapImage? _serverIcon;
        public BitmapImage? ServerIcon { get => _serverIcon; set => SetProperty(ref _serverIcon, value); }

        public ICommand BrowseIconCommand { get; }

        public SettingsGeneralVM(string serverDir, IDialogService dialogService, IAppNavigationService navigationService, Action markDirty)
        {
            _serverDir = serverDir;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _markDirty = markDirty;
            BrowseIconCommand = new RelayCommand(async _ => await BrowseIconAsync());
        }

        public void LoadIcon()
        {
            var iconPath = Path.Combine(_serverDir, "server-icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ServerIcon = bmp;
                }
                catch { ServerIcon = null; }
            }
            else ServerIcon = null;
        }

        public async System.Threading.Tasks.Task BrowseIconAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Server Icon", "Image Files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp");
            if (file != null)
            {
                try
                {
                    // Navigate to the in-app crop page as a detail page
                    var cropPage = new ImageCropPage(file, _navigationService, OnCropComplete);
                    _navigationService.NavigateToDetailPage(
                        cropPage,
                        "Crop Server Icon",
                        DetailRouteKind.ImageCrop,
                        DetailBackNavigation.PreviousDetail);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowMessage("Error", "Failed to open crop editor: " + ex.Message, DialogType.Error);
                }
            }
        }

        private void OnCropComplete(BitmapImage croppedIcon)
        {
            try
            {
                var targetPath = Path.Combine(_serverDir, "server-icon.png");

                // Write the 64x64 PNG to disk
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(croppedIcon));
                using (var fs = new FileStream(targetPath, FileMode.Create))
                {
                    encoder.Save(fs);
                }

                ServerIcon = croppedIcon;
                _markDirty();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("Error", "Failed to save server icon: " + ex.Message, DialogType.Error);
            }
        }
    }
}
