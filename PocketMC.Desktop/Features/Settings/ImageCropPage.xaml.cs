using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;

namespace PocketMC.Desktop.Features.Settings
{
    public partial class ImageCropPage : Page
    {
        private readonly IAppNavigationService _navigationService;
        private readonly Action<BitmapImage> _onCropComplete;
        private BitmapImage _originalImage = null!;
        private double _imageScale = 1.0;
        private Rect _cropRect;
        private bool _isDragging;
        private Point _dragStart;
        private Rect _dragStartRect;

        /// <summary>
        /// Creates a new ImageCropPage.
        /// </summary>
        /// <param name="inputImagePath">Absolute path to the source image file.</param>
        /// <param name="navigationService">App navigation service for back navigation.</param>
        /// <param name="onCropComplete">Callback invoked with the cropped 64x64 BitmapImage on save.</param>
        public ImageCropPage(
            string inputImagePath,
            IAppNavigationService navigationService,
            Action<BitmapImage> onCropComplete)
        {
            InitializeComponent();
            _navigationService = navigationService;
            _onCropComplete = onCropComplete;
            LoadImage(inputImagePath);
        }

        private void LoadImage(string path)
        {
            try
            {
                _originalImage = new BitmapImage();
                _originalImage.BeginInit();
                _originalImage.UriSource = new Uri(path);
                _originalImage.CacheOption = BitmapCacheOption.OnLoad;
                _originalImage.EndInit();
                _originalImage.Freeze();

                SourceImage.Source = _originalImage;
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Error", "Failed to load image: " + ex.Message);
                _navigationService.NavigateBack();
            }
        }

        private void DrawCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_originalImage == null || DrawCanvas.ActualWidth == 0 || DrawCanvas.ActualHeight == 0) return;

            double scaleX = DrawCanvas.ActualWidth / _originalImage.PixelWidth;
            double scaleY = DrawCanvas.ActualHeight / _originalImage.PixelHeight;
            _imageScale = Math.Min(scaleX, scaleY);

            SourceImage.Width = _originalImage.PixelWidth * _imageScale;
            SourceImage.Height = _originalImage.PixelHeight * _imageScale;

            Canvas.SetLeft(SourceImage, (DrawCanvas.ActualWidth - SourceImage.Width) / 2);
            Canvas.SetTop(SourceImage, (DrawCanvas.ActualHeight - SourceImage.Height) / 2);

            if (_cropRect.Width == 0)
            {
                double defaultSize = Math.Min(SourceImage.Width, SourceImage.Height) * 0.75;
                _cropRect = new Rect(
                    Canvas.GetLeft(SourceImage) + (SourceImage.Width - defaultSize) / 2,
                    Canvas.GetTop(SourceImage) + (SourceImage.Height - defaultSize) / 2,
                    defaultSize, defaultSize);
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            Canvas.SetLeft(CropRect, _cropRect.X);
            Canvas.SetTop(CropRect, _cropRect.Y);
            CropRect.Width = _cropRect.Width;
            CropRect.Height = _cropRect.Height;

            Canvas.SetLeft(ResizeThumbBR, _cropRect.Right - ResizeThumbBR.Width / 2);
            Canvas.SetTop(ResizeThumbBR, _cropRect.Bottom - ResizeThumbBR.Height / 2);

            // Darken outside crop area
            var outerGeom = new RectangleGeometry(new Rect(0, 0, DrawCanvas.ActualWidth, DrawCanvas.ActualHeight));
            var innerGeom = new RectangleGeometry(_cropRect);
            OverlayMask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outerGeom, innerGeom);

            // Update live preview
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                double imgLeft = Canvas.GetLeft(SourceImage);
                double imgTop = Canvas.GetTop(SourceImage);

                int pxX = Math.Max(0, (int)((_cropRect.X - imgLeft) / _imageScale));
                int pxY = Math.Max(0, (int)((_cropRect.Y - imgTop) / _imageScale));
                int pxSize = (int)(_cropRect.Width / _imageScale);

                // Clamp
                if (pxX + pxSize > _originalImage.PixelWidth) pxSize = _originalImage.PixelWidth - pxX;
                if (pxY + pxSize > _originalImage.PixelHeight) pxSize = Math.Min(pxSize, _originalImage.PixelHeight - pxY);
                if (pxSize <= 0) return;

                var cropped = new CroppedBitmap(_originalImage, new Int32Rect(pxX, pxY, pxSize, pxSize));
                PreviewImage.Source = cropped;
            }
            catch { /* Ignore transient geometry errors during drag */ }
        }

        #region Drag to move

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.Name == "ResizeThumbBR") return;

            var pos = e.GetPosition(DrawCanvas);
            if (_cropRect.Contains(pos))
            {
                _isDragging = true;
                _dragStart = pos;
                _dragStartRect = _cropRect;
                DrawCanvas.CaptureMouse();
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(DrawCanvas);
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;

            double newX = _dragStartRect.X + dx;
            double newY = _dragStartRect.Y + dy;

            double imgLeft = Canvas.GetLeft(SourceImage);
            double imgTop = Canvas.GetTop(SourceImage);

            newX = Math.Max(imgLeft, Math.Min(newX, imgLeft + SourceImage.Width - _cropRect.Width));
            newY = Math.Max(imgTop, Math.Min(newY, imgTop + SourceImage.Height - _cropRect.Height));

            _cropRect = new Rect(newX, newY, _cropRect.Width, _cropRect.Height);
            UpdateVisuals();
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DrawCanvas.ReleaseMouseCapture();
            }
        }

        #endregion

        #region Resize handle

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double delta = Math.Max(e.HorizontalChange, e.VerticalChange);
            double newSize = _cropRect.Width + delta;

            double imgLeft = Canvas.GetLeft(SourceImage);
            double imgTop = Canvas.GetTop(SourceImage);
            double maxWidth = imgLeft + SourceImage.Width - _cropRect.X;
            double maxHeight = imgTop + SourceImage.Height - _cropRect.Y;
            double maxSize = Math.Min(maxWidth, maxHeight);

            newSize = Math.Max(32, Math.Min(newSize, maxSize));

            _cropRect = new Rect(_cropRect.X, _cropRect.Y, newSize, newSize);
            UpdateVisuals();
        }

        #endregion

        #region Actions

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.NavigateBack();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double imgLeft = Canvas.GetLeft(SourceImage);
                double imgTop = Canvas.GetTop(SourceImage);

                int pxX = Math.Max(0, (int)((_cropRect.X - imgLeft) / _imageScale));
                int pxY = Math.Max(0, (int)((_cropRect.Y - imgTop) / _imageScale));
                int pxSize = (int)(_cropRect.Width / _imageScale);

                // Clamp for safety
                if (pxX + pxSize > _originalImage.PixelWidth) pxSize = _originalImage.PixelWidth - pxX;
                if (pxY + pxSize > _originalImage.PixelHeight) pxSize = Math.Min(pxSize, _originalImage.PixelHeight - pxY);
                if (pxSize <= 0) { PocketMC.Desktop.Infrastructure.AppDialog.ShowWarning("Invalid Crop", "Invalid crop region."); return; }

                // 1. Crop
                var cropped = new CroppedBitmap(_originalImage, new Int32Rect(pxX, pxY, pxSize, pxSize));

                // 2. Resize to exactly 64x64 with high quality interpolation
                double scaleFactor = 64.0 / pxSize;
                var resized = new TransformedBitmap(cropped, new ScaleTransform(scaleFactor, scaleFactor));

                // 3. Encode to PNG in memory
                BitmapImage result = BitmapImageFromSource(resized);

                // 4. Callback with result, then navigate back
                _onCropComplete(result);
                _navigationService.NavigateBack();
            }
            catch (Exception ex)
            {
                PocketMC.Desktop.Infrastructure.AppDialog.ShowError("Error", "Failed to crop image: " + ex.Message);
            }
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

        #endregion
    }
}
