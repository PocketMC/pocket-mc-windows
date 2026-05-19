using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PocketMC.Desktop.Features.Shell
{
    /// <summary>
    /// Extracts the current Windows desktop wallpaper, pre-renders a blurred
    /// version to a frozen bitmap, and sets it as a static background.
    /// Zero GPU cost at runtime — the blur is baked once on apply.
    /// Works on both Windows 10 and 11.
    /// </summary>
    public sealed class WallpaperMicaService : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, System.Text.StringBuilder pvParam, uint fWinIni);
        private const uint SPI_GETDESKWALLPAPER = 0x0073;

        private BitmapSource? _blurredBitmap;
        private string? _lastWallpaperPath;

        /// <summary>
        /// Applies a static pre-blurred wallpaper as the background.
        /// No live effects, no position tracking — just a frozen image fill.
        /// </summary>
        public void Apply(Window window, Image wallpaperImageElement, Border tintOverlay)
        {
            if (window == null || wallpaperImageElement == null) return;

            try
            {
                string? wallpaperPath = GetWallpaperPath();
                if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
                {
                    wallpaperImageElement.Visibility = Visibility.Collapsed;
                    return;
                }

                // Only re-render the blurred bitmap if the wallpaper changed
                if (_blurredBitmap == null || _lastWallpaperPath != wallpaperPath)
                {
                    _blurredBitmap = CreatePreBlurredBitmap(wallpaperPath);
                    _lastWallpaperPath = wallpaperPath;
                }

                if (_blurredBitmap == null)
                {
                    wallpaperImageElement.Visibility = Visibility.Collapsed;
                    return;
                }

                // Static fill — no effects, no position tracking, no events
                wallpaperImageElement.Source = _blurredBitmap;
                wallpaperImageElement.Stretch = Stretch.UniformToFill;
                wallpaperImageElement.HorizontalAlignment = HorizontalAlignment.Center;
                wallpaperImageElement.VerticalAlignment = VerticalAlignment.Center;
                wallpaperImageElement.Visibility = Visibility.Visible;
                wallpaperImageElement.IsHitTestVisible = false;
                wallpaperImageElement.Effect = null; // No live GPU effect
                wallpaperImageElement.Margin = new Thickness(0);

                // Dark tint overlay for text readability
                if (tintOverlay != null)
                {
                    tintOverlay.Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x18, 0x18, 0x18));
                    tintOverlay.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                wallpaperImageElement.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Hides the wallpaper layer.
        /// </summary>
        public void Detach(Window window, Image wallpaperImageElement)
        {
            if (wallpaperImageElement == null) return;
            wallpaperImageElement.Visibility = Visibility.Collapsed;
            wallpaperImageElement.Source = null;
            wallpaperImageElement.Effect = null;
        }

        /// <summary>
        /// Loads the wallpaper at a small resolution and bakes a Gaussian blur
        /// into the pixels via RenderTargetBitmap. The result is a frozen
        /// BitmapSource with zero ongoing GPU cost.
        /// </summary>
        private static BitmapSource? CreatePreBlurredBitmap(string path)
        {
            try
            {
                // Load at a small size — we're blurring it heavily, detail is irrelevant
                const int decodeWidth = 800;
                BitmapImage? source = LoadBitmap(path, decodeWidth);
                if (source == null) return null;

                // Render the bitmap with a blur effect baked in
                var image = new Image
                {
                    Source = source,
                    Stretch = Stretch.UniformToFill,
                    Effect = new BlurEffect
                    {
                        Radius = 80,
                        RenderingBias = RenderingBias.Performance
                    }
                };

                // Measure/arrange so WPF can render it
                int width = source.PixelWidth;
                int height = source.PixelHeight;
                image.Measure(new Size(width, height));
                image.Arrange(new Rect(0, 0, width, height));
                image.UpdateLayout();

                // Render to a bitmap — this bakes the blur into pixels
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(image);
                rtb.Freeze();

                return rtb;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage? LoadBitmap(string path, int decodeWidth)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = decodeWidth;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                // Fallback: stream-based loading for exotic formats
                try
                {
                    using var stream = File.OpenRead(path);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = stream;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = decodeWidth;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the current desktop wallpaper file path.
        /// Registry → SPI fallback → TranscodedWallpaper fallback.
        /// </summary>
        private static string? GetWallpaperPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                string? path = key?.GetValue("Wallpaper") as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch { }

            try
            {
                var sb = new System.Text.StringBuilder(512);
                SystemParametersInfo(SPI_GETDESKWALLPAPER, 512, sb, 0);
                string path = sb.ToString();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch { }

            try
            {
                string transcodedPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
                if (File.Exists(transcodedPath))
                    return transcodedPath;
            }
            catch { }

            return null;
        }

        public void Dispose()
        {
            _blurredBitmap = null;
            _lastWallpaperPath = null;
        }
    }
}
