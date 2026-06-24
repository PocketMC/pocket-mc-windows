using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PocketMC.Desktop.Infrastructure
{
    public class ImageProcessingService : IImageProcessingService
    {
        public BitmapImage CropAndResizeImage(BitmapImage originalImage, int pxX, int pxY, int pxSize, int targetSize = 64)
        {
            // Clamp for safety
            if (pxX + pxSize > originalImage.PixelWidth) pxSize = originalImage.PixelWidth - pxX;
            if (pxY + pxSize > originalImage.PixelHeight) pxSize = Math.Min(pxSize, originalImage.PixelHeight - pxY);
            if (pxSize <= 0)
            {
                throw new InvalidOperationException("Invalid crop region.");
            }

            // 1. Crop
            var cropped = new CroppedBitmap(originalImage, new Int32Rect(pxX, pxY, pxSize, pxSize));

            // 2. Resize to exactly targetSize x targetSize with high quality interpolation
            double scaleFactor = (double)targetSize / pxSize;
            var resized = new TransformedBitmap(cropped, new ScaleTransform(scaleFactor, scaleFactor));

            // 3. Encode to PNG in memory
            return BitmapImageFromSource(resized);
        }

        private static BitmapImage BitmapImageFromSource(BitmapSource source)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(ms);
            ms.Position = 0;

            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = ms;
            result.EndInit();
            result.Freeze();
            return result;
        }
    }
}
