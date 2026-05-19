using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
using PocketMC.Desktop.Infrastructure;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Shell
{
    public sealed class ShellVisualService : IShellVisualService, IDisposable
    {
        private readonly ApplicationState _applicationState;
        private FluentWindow? _boundWindow;
        private System.Windows.Controls.Image? _micaFallbackImage;

        public ShellVisualService(ApplicationState applicationState)
        {
            _applicationState = applicationState;
        }

        public void Attach(FluentWindow window, System.Windows.Controls.Image micaFallbackImage)
        {
            _boundWindow = window;
            _micaFallbackImage = micaFallbackImage;
            ApplyTheme();
        }

        public void RequestMicaUpdate()
        {
            if (_boundWindow == null) return;
            if (_micaFallbackImage != null) _micaFallbackImage.Visibility = Visibility.Collapsed;

            string backdrop = _applicationState.Settings.WindowBackdrop ?? "Acrylic";

            ApplyTheme(); // Update light/dark state before changing backdrop

            if (backdrop == "Mica" && WallpaperMicaService.IsWindows11OrLater)
            {
                _boundWindow.WindowBackdropType = WindowBackdropType.Mica;
            }
            else if (backdrop == "Acrylic" || backdrop == "Mica")
            {
                // Always use custom wallpaper fallback for Acrylic to keep it active when unfocused
                _boundWindow.WindowBackdropType = WindowBackdropType.None;
                ApplyWallpaperFallback();
            }
            else
            {
                _boundWindow.WindowBackdropType = WindowBackdropType.None;
            }
        }

        private void ApplyWallpaperFallback()
        {
            if (_boundWindow == null || _micaFallbackImage == null) return;

            var w = (int)Math.Max(_boundWindow.ActualWidth, SystemParameters.PrimaryScreenWidth);
            var h = (int)Math.Max(_boundWindow.ActualHeight, SystemParameters.PrimaryScreenHeight);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var bg = WallpaperMicaService.CreateMicaBackground(
                        targetWidth: w,
                        targetHeight: h,
                        blurRadius: 120, // Increased by 50% for stronger effect
                        tintOpacity: 0.82,
                        tintColor: Color.FromRgb(32, 32, 32));

                    _boundWindow.Dispatcher.Invoke(() =>
                    {
                        if (bg != null)
                        {
                            _micaFallbackImage.Source = bg;
                            _micaFallbackImage.Visibility = Visibility.Visible;
                        }
                    });
                }
                catch { /* Ignore fallback failures */ }
            });
        }

        public void ApplyTheme(string theme = "Dark")
        {
            if (_boundWindow == null) return;
            
            if (_boundWindow.IsLoaded)
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(_boundWindow);
            }

            string backdrop = _applicationState.Settings.WindowBackdrop ?? "Acrylic";
            if (backdrop == "Light")
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            }
            else
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
        }

        public void Dispose()
        {
        }
    }
}
