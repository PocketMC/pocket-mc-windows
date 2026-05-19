using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Features.Shell.Native;
using Wpf.Ui.Controls;

namespace PocketMC.Desktop.Features.Shell
{
    public sealed class ShellVisualService : IShellVisualService, IDisposable
    {
        private const string SolidDarkFallback = "#FF242424";
        private const string AcrylicActiveTint = "#CC202020";
        private const string MicaActiveTint = "#B8202020";
        private const string BlurActiveTint = "#D0202020";
        private const string SolidLightFallback = "#FFF7F7F7";
        private const string TransparentTint = "#00FFFFFF";
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

        private readonly ApplicationState _applicationState;
        private FluentWindow? _boundWindow;
        private IntPtr _boundHwnd;
        private bool _isWindowActive = true;
        private bool _isNativeBlurActive;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public ShellVisualService(ApplicationState applicationState)
        {
            _applicationState = applicationState;
        }

        public void Attach(FluentWindow window)
        {
            _boundWindow = window;

            // Capture the HWND once the window has a valid handle.
            if (window.IsLoaded)
            {
                _boundHwnd = new WindowInteropHelper(window).Handle;
            }
            else
            {
                window.Loaded += (_, _) =>
                {
                    _boundHwnd = new WindowInteropHelper(window).Handle;
                };
            }

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
                    DisableNativeBlurIfActive();
                    ApplySolidFallback(window, SolidDarkFallback);
                    return;
                }

                if (backdrop.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    DisableNativeBlurIfActive();
                    window.WindowBackdropType = WindowBackdropType.None;
                    window.Background = CreateBrush(SolidLightFallback);
                    SetTintLayer(window, TransparentTint);
                    return;
                }

                // Explicit Blur selection — works on all OS versions.
                if (backdrop.Equals("Blur", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyNativeBlur(window);
                    return;
                }

                // Mica — Windows 11 only; fallback to native blur on Windows 10.
                if (backdrop.Equals("Mica", StringComparison.OrdinalIgnoreCase))
                {
                    if (Environment.OSVersion.Version.Build >= 22000)
                    {
                        DisableNativeBlurIfActive();
                        window.WindowBackdropType = WindowBackdropType.Mica;
                        window.Background = Brushes.Transparent;
                        SetTintLayer(window, MicaActiveTint);
                        return;
                    }

                    // Windows 10: Mica not supported — use native blur instead of broken overlay.
                    ApplyNativeBlur(window);
                    return;
                }

                // Acrylic — Windows 11 only; fallback to native blur on Windows 10.
                if (backdrop.Equals("Acrylic", StringComparison.OrdinalIgnoreCase))
                {
                    if (Environment.OSVersion.Version.Build >= 22000)
                    {
                        DisableNativeBlurIfActive();
                        window.WindowBackdropType = WindowBackdropType.Acrylic;
                        window.Background = Brushes.Transparent;
                        SetTintLayer(window, AcrylicActiveTint);
                        return;
                    }

                    // Windows 10: Acrylic not supported — use native blur instead of broken overlay.
                    ApplyNativeBlur(window);
                    return;
                }

                // "None" / Solid Dark or any unrecognised value.
                DisableNativeBlurIfActive();
                ApplySolidFallback(window, SolidDarkFallback);
            }
            catch
            {
                ApplySolidFallbackBestEffort(window);
            }
        }

        public void ApplyTheme(string theme = "Dark")
        {
            var window = _boundWindow;
            if (window == null) return;

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(() => ApplyTheme(theme));
                return;
            }

            try
            {
                if (window.IsLoaded)
                {
                    Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(window);
                }

                string backdrop = _applicationState.Settings.WindowBackdrop ?? "Acrylic";
                bool explicitLightMode = backdrop.Equals("Light", StringComparison.OrdinalIgnoreCase);
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                    explicitLightMode
                        ? Wpf.Ui.Appearance.ApplicationTheme.Light
                        : Wpf.Ui.Appearance.ApplicationTheme.Dark);
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

        /// <summary>
        /// Applies native Windows 10 blur using SetWindowCompositionAttribute,
        /// sets the window background to transparent, and applies the dark tint layer.
        /// </summary>
        private void ApplyNativeBlur(FluentWindow window)
        {
            window.WindowBackdropType = WindowBackdropType.None;
            window.Background = Brushes.Transparent;
            SetTintLayer(window, BlurActiveTint);

            if (_boundHwnd != IntPtr.Zero)
            {
                Windows10Blur.Enable(_boundHwnd);
                _isNativeBlurActive = true;
            }
        }

        /// <summary>
        /// Disables native blur if it is currently active. Safe to call when blur is not active.
        /// </summary>
        private void DisableNativeBlurIfActive()
        {
            if (_isNativeBlurActive && _boundHwnd != IntPtr.Zero)
            {
                Windows10Blur.Disable(_boundHwnd);
                _isNativeBlurActive = false;
            }
        }

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
            DisableNativeBlurIfActive();
        }
    }
}
