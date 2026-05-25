using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PocketMC.Desktop.Features.Mods
{
    public static class AddonIconService
    {
        private static readonly ConcurrentDictionary<(string Path, DateTime LastWriteTime), ImageSource> _iconCache = new();

        private static ImageSource? _fabricFallback;
        private static ImageSource? _quiltFallback;
        private static ImageSource? _forgeFallback;
        private static ImageSource? _pluginFallback;
        private static ImageSource? _bedrockFallback;
        private static ImageSource? _unknownFallback;

        public static ImageSource FabricFallback => _fabricFallback ??= CreateDrawingIcon(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7B5BCA")), "F");
        public static ImageSource QuiltFallback => _quiltFallback ??= CreateDrawingIcon(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4E75")), "Q");
        public static ImageSource ForgeFallback => _forgeFallback ??= CreateDrawingIcon(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DF6E26")), "M"); // M for Mod
        public static ImageSource PluginFallback => _pluginFallback ??= CreateDrawingIcon(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E75B6")), "P"); // P for Plugin
        public static ImageSource BedrockFallback => _bedrockFallback ??= CreateDrawingIcon(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A8F46")), "B"); // B for Bedrock
        public static ImageSource UnknownFallback => _unknownFallback ??= CreateDrawingIcon(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F7F7F")), "?");

        public static ImageSource? GetIconFromBytes(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CreateOptions = BitmapCreateOptions.None;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze(); // Freeze so it can be used on the main thread
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource GetIcon(string filePath, string loaderType, byte[]? customIconBytes)
        {
            var lastWriteTime = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.MinValue;
            var cacheKey = (filePath, lastWriteTime);

            if (_iconCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            ImageSource? icon = GetIconFromBytes(customIconBytes);
            if (icon == null)
            {
                icon = loaderType.ToLowerInvariant() switch
                {
                    "fabric" => FabricFallback,
                    "quilt" => QuiltFallback,
                    "forge" => ForgeFallback,
                    "neoforge" => ForgeFallback, // NeoForge shares mod fallback
                    "plugin" => PluginFallback,
                    _ => UnknownFallback
                };
            }

            _iconCache[cacheKey] = icon;
            return icon;
        }

        private static ImageSource CreateDrawingIcon(Brush backgroundBrush, string text)
        {
            var drawingGroup = new DrawingGroup();

            // 1. Background Rounded Rectangle
            var bgGeometry = new RectangleGeometry(new Rect(0, 0, 48, 48), 8, 8);
            var bgDrawing = new GeometryDrawing(backgroundBrush, null, bgGeometry);
            drawingGroup.Children.Add(bgDrawing);

            // 2. Text / Symbol
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI, Arial, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                26,
                Brushes.White,
                1.0); // Safe fallback DPI independent

            formattedText.TextAlignment = TextAlignment.Center;

            // Align vertical position in the middle
            double yOffset = 24.0 - formattedText.Height / 2.0;
            var textGeometry = formattedText.BuildGeometry(new Point(24.0, yOffset - 1.0));
            var textDrawing = new GeometryDrawing(Brushes.White, null, textGeometry);
            drawingGroup.Children.Add(textDrawing);

            var drawingImage = new DrawingImage(drawingGroup);
            drawingImage.Freeze();
            return drawingImage;
        }
    }
}
