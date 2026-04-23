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

        public bool EnableMicaEffect { get; set; }

        public ShellVisualService(ApplicationState applicationState)
        {
            _applicationState = applicationState;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public void Attach(FluentWindow window, System.Windows.Controls.Image micaFallbackImage)
        {
            _boundWindow = window;
            _micaFallbackImage = micaFallbackImage;
            ApplyTheme(_applicationState.Settings.ApplicationTheme);
        }

        public void RequestMicaUpdate()
        {
            if (_boundWindow == null) return;

            bool enableMica = _applicationState.Settings.EnableMicaEffect;
            EnableMicaEffect = enableMica;

            if (WallpaperMicaService.IsWindows11OrLater)
            {
                _boundWindow.WindowBackdropType = enableMica
                    ? WindowBackdropType.Mica
                    : WindowBackdropType.None;
            }
            else
            {
                if (enableMica)
                {
                    ApplyWin10MicaFallback();
                }
                else if (_micaFallbackImage != null)
                {
                    _micaFallbackImage.Visibility = Visibility.Collapsed;
                }
            }
        }

        public void ApplyTheme(string theme)
        {
            if (_boundWindow == null) return;
            
            if (theme == "Light")
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(_boundWindow);
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            }
            else if (theme == "Dark")
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(_boundWindow);
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
            else
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(_boundWindow);
                Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            }
        }

        private void ApplyWin10MicaFallback()
        {
            if (_boundWindow == null || _micaFallbackImage == null) return;
            if (WallpaperMicaService.IsWindows11OrLater || !_applicationState.Settings.EnableMicaEffect) return;

            var w = (int)Math.Max(_boundWindow.ActualWidth, SystemParameters.PrimaryScreenWidth);
            var h = (int)Math.Max(_boundWindow.ActualHeight, SystemParameters.PrimaryScreenHeight);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var bg = WallpaperMicaService.CreateMicaBackground(
                        targetWidth: w,
                        targetHeight: h,
                        blurRadius: 80,
                        tintOpacity: 0.78,
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

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Desktop)
                ApplyWin10MicaFallback();
        }

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
