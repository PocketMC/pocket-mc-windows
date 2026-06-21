using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Shell
{
    public sealed class ShellVisualService : IShellVisualService, IDisposable
    {
        private const string SolidDarkFallback = "#FF242424";
        private const string AcrylicActiveTint = "#CC202020";
        private const string MicaActiveTint = "#B8202020";
        private const string SolidLightFallback = "#FFF7F7F7";
        private const string TransparentTint = "#00FFFFFF";
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

        private readonly ApplicationState _applicationState;
        private readonly WindowsCornerService _windowsCornerService;
        private readonly WallpaperMicaService _wallpaperMicaService;
        private readonly AccentColorService _accentColorService;
        private FluentWindow? _boundWindow;
        private bool _isWindowActive = true;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public ShellVisualService(
            ApplicationState applicationState,
            WindowsCornerService windowsCornerService,
            AccentColorService accentColorService)
        {
            _applicationState = applicationState;
            _windowsCornerService = windowsCornerService;
            _accentColorService = accentColorService;
            _wallpaperMicaService = new WallpaperMicaService();
        }

        public void Attach(FluentWindow window)
        {
            _boundWindow = window;
            ApplyTheme();
            RequestMicaUpdate();
        }

        public void RequestMicaUpdate()
        {
            var window = _boundWindow;
            if (window == null) return;

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(RequestMicaUpdate);
                return;
            }

            try
            {
                ApplyTheme();
                ApplyDwmDarkMode(window);

                string backdrop = _applicationState.Settings.WindowBackdrop ?? "Acrylic";

                if (!_isWindowActive)
                {
                    HideFakeMicaLayer(window);
                    ApplySolidFallback(window, SolidDarkFallback);
                    return;
                }

                if (backdrop.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    HideFakeMicaLayer(window);
                    window.WindowBackdropType = WindowBackdropType.None;
                    window.Background = CreateBrush(SolidLightFallback);
                    SetTintLayer(window, TransparentTint);
                    return;
                }

                if (backdrop.Equals("FakeMica", StringComparison.OrdinalIgnoreCase))
                {
                    // Fake Mica: use custom image or wallpaper-blur background
                    window.WindowBackdropType = WindowBackdropType.None;
                    window.Background = CreateBrush("#FF1A1A1A");
                    SetTintLayer(window, TransparentTint);

                    string? customImagePath = _applicationState.Settings.CustomBackgroundImagePath;
                    if (!string.IsNullOrWhiteSpace(customImagePath))
                    {
                        // Try custom image first; fall back to wallpaper if it fails
                        if (!ApplyFakeMicaLayerWithCustomImage(window, customImagePath))
                        {
                            ApplyFakeMicaLayer(window);
                        }
                    }
                    else
                    {
                        ApplyFakeMicaLayer(window);
                    }
                    return;
                }

                // For native Mica/Acrylic, hide the fake mica layer
                HideFakeMicaLayer(window);

                if (backdrop.Equals("Mica", StringComparison.OrdinalIgnoreCase) &&
                    _windowsCornerService.IsWindows11())
                {
                    window.WindowBackdropType = WindowBackdropType.Mica;
                    window.Background = Brushes.Transparent;
                    SetTintLayer(window, MicaActiveTint);
                    return;
                }

                if (backdrop.Equals("Acrylic", StringComparison.OrdinalIgnoreCase) &&
                    _windowsCornerService.IsWindows11())
                {
                    window.WindowBackdropType = WindowBackdropType.Acrylic;
                    window.Background = Brushes.Transparent;
                    SetTintLayer(window, AcrylicActiveTint);
                    return;
                }

                ApplySolidFallback(window, SolidDarkFallback);
            }
            catch
            {
                ApplySolidFallbackBestEffort(window);
            }
        }

        public void ApplyTheme()
        {
            var window = _boundWindow;
            if (window == null) return;

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(ApplyTheme);
                return;
            }

            try
            {
                string backdrop = _applicationState.Settings.WindowBackdrop ?? "Acrylic";
                bool explicitLightMode = backdrop.Equals("Light", StringComparison.OrdinalIgnoreCase);
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                    explicitLightMode
                        ? Wpf.Ui.Appearance.ApplicationTheme.Light
                        : Wpf.Ui.Appearance.ApplicationTheme.Dark,
                    Wpf.Ui.Controls.WindowBackdropType.None,
                    updateAccent: false);

                _accentColorService.ApplyCurrentAccent();
            }
            catch
            {
                // Visual polish should never block startup or server control.
            }
        }

        public void SetWindowActive(bool isActive)
        {
            _isWindowActive = isActive;
            RequestMicaUpdate();
        }

        // ── Fake Mica Helpers ──────────────────────────────────────────

        private void ApplyFakeMicaLayer(FluentWindow window)
        {
            try
            {
                var imageElement = window.FindName("WallpaperBlurLayer") as System.Windows.Controls.Image;
                var tintOverlay = window.FindName("WallpaperTintOverlay") as Border;
                if (imageElement == null || tintOverlay == null) return;

                _wallpaperMicaService.Apply(window, imageElement, tintOverlay);
            }
            catch
            {
                // Non-critical — fall through silently
            }
        }

        private bool ApplyFakeMicaLayerWithCustomImage(FluentWindow window, string customImagePath)
        {
            try
            {
                var imageElement = window.FindName("WallpaperBlurLayer") as System.Windows.Controls.Image;
                var tintOverlay = window.FindName("WallpaperTintOverlay") as Border;
                if (imageElement == null || tintOverlay == null) return false;

                return _wallpaperMicaService.ApplyCustomImage(window, imageElement, tintOverlay, customImagePath);
            }
            catch
            {
                return false;
            }
        }

        private void HideFakeMicaLayer(FluentWindow window)
        {
            try
            {
                var imageElement = window.FindName("WallpaperBlurLayer") as System.Windows.Controls.Image;
                var tintOverlay = window.FindName("WallpaperTintOverlay") as Border;
                if (imageElement != null)
                {
                    _wallpaperMicaService.Detach(window, imageElement);
                }
                if (tintOverlay != null)
                {
                    tintOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        // ── Existing Helpers ──────────────────────────────────────────

        private static void ApplyDwmDarkMode(FluentWindow window)
        {
            try
            {
                if (!window.IsLoaded) return;

                var helper = new WindowInteropHelper(window);
                if (helper.Handle == IntPtr.Zero) return;

                int isDark = 1;
                int size = sizeof(int);
                DwmSetWindowAttribute(helper.Handle, DwmUseImmersiveDarkMode, ref isDark, size);
                DwmSetWindowAttribute(helper.Handle, DwmUseImmersiveDarkModeBefore20H1, ref isDark, size);
            }
            catch
            {
                // Best-effort only. Older Windows builds or hosted previews may reject this.
            }
        }

        private static void ApplySolidFallback(FluentWindow window, string color)
        {
            window.WindowBackdropType = WindowBackdropType.None;
            window.Background = CreateBrush(color);
            SetTintLayer(window, color);
        }

        private static void ApplySolidFallbackBestEffort(FluentWindow window)
        {
            try
            {
                ApplySolidFallback(window, SolidDarkFallback);
            }
            catch
            {
                // Nothing else to do; failures here must not crash the shell.
            }
        }

        private static void SetTintLayer(FluentWindow window, string color)
        {
            if (window.FindName("BackdropTintLayer") is Border tintLayer)
            {
                tintLayer.Background = CreateBrush(color);
            }
        }

        private static SolidColorBrush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
            brush.Freeze();
            return brush;
        }

        public void Dispose()
        {
            _wallpaperMicaService.Dispose();
        }
    }
}
