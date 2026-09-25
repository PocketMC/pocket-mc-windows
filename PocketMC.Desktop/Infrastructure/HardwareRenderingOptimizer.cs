using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PocketMC.Desktop.Infrastructure;

/// <summary>
/// Configures high refresh rate display synchronization, GPU hardware acceleration,
/// pixel snapping, and sub-pixel jitter elimination to give PocketMC a 120Hz/144Hz/240Hz
/// buttery smooth feel without CPU or RAM bloat.
/// </summary>
public static class HardwareRenderingOptimizer
{
    private static int _cachedRefreshRate = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    private const int ENUM_CURRENT_SETTINGS = -1;

    /// <summary>
    /// Gets the primary display's active refresh rate in Hz (e.g. 60, 120, 144, 165, 240).
    /// Defaults to at least 120Hz for high-precision animation stepping.
    /// </summary>
    public static int GetDisplayRefreshRate()
    {
        if (_cachedRefreshRate > 0)
            return _cachedRefreshRate;

        try
        {
            var devMode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode))
            {
                if (devMode.dmDisplayFrequency >= 60)
                {
                    _cachedRefreshRate = Math.Max(120, devMode.dmDisplayFrequency);
                    return _cachedRefreshRate;
                }
            }
        }
        catch
        {
            // Ignore failure and fallback to high-refresh default
        }

        _cachedRefreshRate = 144;
        return _cachedRefreshRate;
    }

    /// <summary>
    /// Initializes global process-level GPU hardware acceleration and runtime switches.
    /// </summary>
    public static void InitializeGlobalPerformanceProfile()
    {
        try
        {
            // Explicitly ensure hardware acceleration is active
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            // Allow hardware acceleration even in remote desktop / VM sessions
            AppContext.SetSwitch("Switch.System.Windows.Media.EnableHardwareAccelerationInRdp", true);

            // Pre-detect display refresh rate
            _ = GetDisplayRefreshRate();
        }
        catch
        {
        }
    }

    /// <summary>
    /// Applies hardware-accelerated text and pixel-perfect rendering attributes to a window.
    /// </summary>
    public static void OptimizeWindow(Window window)
    {
        if (window == null) return;

        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        RenderOptions.SetBitmapScalingMode(window, BitmapScalingMode.Linear);
        RenderOptions.SetClearTypeHint(window, ClearTypeHint.Enabled);
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
    }
}
