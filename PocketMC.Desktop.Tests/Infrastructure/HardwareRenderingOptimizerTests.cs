using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PocketMC.Desktop.Infrastructure;
using Xunit;

namespace PocketMC.Desktop.Tests.Infrastructure;

public sealed class HardwareRenderingOptimizerTests
{
    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void GetDisplayRefreshRate_ReturnsAtLeast120Hz()
    {
        int hz = HardwareRenderingOptimizer.GetDisplayRefreshRate();
        Assert.True(hz >= 120, $"Expected display refresh rate to be at least 120Hz for high-refresh interpolation, got {hz}Hz");
    }

    [Fact]
    public void InitializeGlobalPerformanceProfile_EnsuresDefaultHardwareRenderMode()
    {
        HardwareRenderingOptimizer.InitializeGlobalPerformanceProfile();
        Assert.Equal(RenderMode.Default, RenderOptions.ProcessRenderMode);
    }

    [Fact]
    public void OptimizeWindow_ConfiguresHighPerformanceVisualSettings()
    {
        RunInSta(() =>
        {
            var window = new Window();
            HardwareRenderingOptimizer.OptimizeWindow(window);

            Assert.True(window.UseLayoutRounding);
            Assert.True(window.SnapsToDevicePixels);
            Assert.Equal(BitmapScalingMode.Linear, RenderOptions.GetBitmapScalingMode(window));
            Assert.Equal(ClearTypeHint.Enabled, RenderOptions.GetClearTypeHint(window));
            Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(window));
            Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(window));
        });
    }

    [Fact]
    public void ScrollViewerHelper_EnableMouseWheelScrolling_AttachesAndDetachesCleanly()
    {
        RunInSta(() =>
        {
            var page = new Page();
            var scrollViewer = new ScrollViewer();

            ScrollViewerHelper.EnableMouseWheelScrolling(page, scrollViewer);

            // Should detach without exception
            ScrollViewerHelper.DisableMouseWheelScrolling(page);
        });
    }

    [Fact]
    public void ScrollViewerHelper_ScrollByWheelDelta_DirectControl_HandlesDeltaWithoutGlitch()
    {
        RunInSta(() =>
        {
            var scrollViewer = new ScrollViewer();

            // Direct call should execute cleanly with 0 lag/exception
            ScrollViewerHelper.ScrollByWheelDelta(scrollViewer, 120);
            ScrollViewerHelper.ScrollByWheelDelta(scrollViewer, -120);
            ScrollViewerHelper.ScrollByWheelDelta(scrollViewer, 0);
        });
    }
}
