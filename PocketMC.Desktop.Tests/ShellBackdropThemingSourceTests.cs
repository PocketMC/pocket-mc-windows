namespace PocketMC.Desktop.Tests;

public sealed class ShellBackdropThemingSourceTests
{
    [Fact]
    public void MainWindow_DoesNotHardcodeMicaBackdropAndDefinesTintLayer()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "MainWindow.xaml"));

        Assert.DoesNotContain("WindowBackdropType=\"Mica\"", xaml);
        Assert.Contains("Background=\"#FF242424\"", xaml);
        Assert.Contains("x:Name=\"BackdropTintLayer\"", xaml);
        Assert.Contains("Background=\"#CC202020\"", xaml);
        Assert.Contains("Activated=\"Window_Activated\"", xaml);
        Assert.Contains("Deactivated=\"Window_Deactivated\"", xaml);
    }

    [Fact]
    public void ShellVisualServiceContract_ExposesWindowActivationState()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "Interfaces",
            "IShellVisualService.cs"));

        Assert.Contains("SetWindowActive(bool isActive)", source);
    }

    [Fact]
    public void ShellVisualService_InactiveStateDisablesBackdropAndUsesDarkFallback()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "ShellVisualService.cs"));

        Assert.Contains("DwmSetWindowAttribute", source);
        Assert.Contains("WindowBackdropType.None", source);
        Assert.Contains("#FF242424", source);
    }

    [Fact]
    public void ShellVisualService_UsesSharedWindowsBuildDetection()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "ShellVisualService.cs"));

        Assert.Contains("WindowsCornerService", source);
        Assert.Contains("_windowsCornerService.IsWindows11()", source);
        Assert.DoesNotContain("Environment.OSVersion.Version.Build >= 22000", source);
    }

    [Fact]
    public void WindowsCornerService_UsesRegistryBuildNumberAndVisualClipping()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "WindowsCornerService.cs"));

        Assert.Contains(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", source);
        Assert.Contains("CurrentBuildNumber", source);
        Assert.Contains("Windows11MinimumBuild = 22000", source);
        Assert.Contains("ApplyWindows10RoundedCorners(Window window)", source);
        // Uses visual-tree clipping, not a template or AllowsTransparency
        Assert.Contains("RectangleGeometry", source);
        Assert.Contains("VisualTreeHelper", source);
        Assert.Contains("rootVisual.Clip", source);
        Assert.DoesNotContain("AllowsTransparencyProperty", source);
        // Uses Win32 region to eliminate white corner artifacts
        Assert.Contains("CreateRoundRectRgn", source);
        Assert.Contains("SetWindowRgn", source);
    }

    [Fact]
    public void WindowsCornerService_RemovesClipWhenMaximized()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "WindowsCornerService.cs"));

        Assert.Contains("WindowState.Maximized", source);
        Assert.Contains("rootVisual.Clip = null", source);
    }

    [Fact]
    public void WindowsCornerService_RegistersGlobalWindowHookForAllDialogs()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "WindowsCornerService.cs"));

        Assert.Contains("RegisterGlobalWindowHook()", source);
        Assert.Contains("RegisterClassHandler", source);
        Assert.Contains("typeof(Window)", source);
        Assert.Contains("FrameworkElement.LoadedEvent", source);
    }

    [Fact]
    public void AppStartup_CallsRegisterGlobalWindowHook()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "App.xaml.cs"));

        Assert.Contains("RegisterGlobalWindowHook()", source);
    }

    [Fact]
    public void MainWindow_AppliesWindows10CornerServiceOnlyFromCodeBehind()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "MainWindow.xaml.cs"));

        Assert.Contains("WindowsCornerService", source);
        Assert.Contains("ApplyWindows10RoundedCorners(this)", source);
    }
}
