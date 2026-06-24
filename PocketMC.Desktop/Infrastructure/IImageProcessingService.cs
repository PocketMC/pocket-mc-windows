using System.Windows.Media.Imaging;

namespace PocketMC.Desktop.Infrastructure
{
    public interface IImageProcessingService
    {
        BitmapImage CropAndResizeImage(BitmapImage originalImage, int pxX, int pxY, int pxSize, int targetSize = 64);
    }
}
