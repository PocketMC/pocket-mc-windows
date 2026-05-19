using System;
using System.Runtime.InteropServices;

namespace PocketMC.Desktop.Features.Shell.Native
{
    /// <summary>
    /// Applies native Windows 10 blur-behind using SetWindowCompositionAttribute.
    /// Safe best-effort: failures are silently swallowed so the app never crashes.
    /// </summary>
    internal static class Windows10Blur
    {
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;   // AABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// Enables blur-behind on the given window handle.
        /// Uses a near-transparent black gradient so the visible tint is controlled
        /// by the BackdropTintLayer in XAML, not by DWM.
        /// </summary>
        public static void Enable(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;

                var accent = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_BLURBEHIND,
                    AccentFlags = 0,
                    GradientColor = 0x01000000,   // near-transparent black (AABBGGRR)
                    AnimationId = 0
                };

                SetAccent(hwnd, ref accent);
            }
            catch
            {
                // Best-effort only — older builds or hosted environments may reject this.
            }
        }

        /// <summary>
        /// Disables the blur effect, returning the window to a normal composition state.
        /// </summary>
        public static void Disable(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;

                var accent = new AccentPolicy
                {
                    AccentState = ACCENT_DISABLED,
                    AccentFlags = 0,
                    GradientColor = 0,
                    AnimationId = 0
                };

                SetAccent(hwnd, ref accent);
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static void SetAccent(IntPtr hwnd, ref AccentPolicy accent)
        {
            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };

                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}
