namespace PocketMC.Desktop.Tests;

public sealed class ScrollBehaviorSourceTests
{
    [Theory]
    [InlineData("Dashboard/DashboardPage.xaml", "DashboardScrollViewer")]
    [InlineData("Tunnel/TunnelPage.xaml", "TunnelListScrollViewer")]
    [InlineData("Tunnel/PortsMapPage.xaml", "PortsMapScrollViewer")]
    [InlineData("RemoteControl/UI/RemoteControlPage.xaml", "RemoteControlScrollViewer")]
    [InlineData("Setup/JavaSetupPage.xaml", "RuntimeScrollViewer")]
    [InlineData("Setup/AppSettingsPage.xaml", "MainScrollViewer")]
    [InlineData("Shell/AboutPage.xaml", "AboutScrollViewer")]
    public void AffectedPages_NameTheScrollViewerThatOwnsWheelInput(string relativeFeaturePath, string scrollViewerName)
    {
        string xaml = ReadFeatureFile(relativeFeaturePath);

        Assert.Contains($"x:Name=\"{scrollViewerName}\"", xaml);
    }

    [Theory]
    [InlineData("Dashboard/DashboardPage.xaml.cs", "DashboardScrollViewer")]
    [InlineData("Tunnel/TunnelPage.xaml.cs", "TunnelListScrollViewer")]
    [InlineData("Tunnel/PortsMapPage.xaml.cs", "PortsMapScrollViewer")]
    [InlineData("RemoteControl/UI/RemoteControlPage.xaml.cs", "RemoteControlScrollViewer")]
    [InlineData("Setup/JavaSetupPage.xaml.cs", "RuntimeScrollViewer")]
    [InlineData("Setup/AppSettingsPage.xaml.cs", "MainScrollViewer")]
    [InlineData("Shell/AboutPage.xaml.cs", "AboutScrollViewer")]
    public void AffectedPages_UseTheSharedWheelForwarder(string relativeFeaturePath, string scrollViewerName)
    {
        string source = ReadFeatureFile(relativeFeaturePath);

        Assert.Contains("ScrollViewerHelper.EnableMouseWheelScrolling", source);
        Assert.Contains("ScrollViewerHelper.DisableMouseWheelScrolling", source);
        Assert.Contains(scrollViewerName, source);
    }

    [Fact]
    public void MainWindow_DisablesTheShellScrollHostForAffectedPagesOnly()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Shell",
            "MainWindow.xaml.cs"));

        string managedPageTypes = ExtractBetween(
            source,
            "private static readonly HashSet<Type> ShellOwnedScrollPageTypes",
            "public MainWindow");

        Assert.Contains("typeof(DashboardPage)", managedPageTypes);
        Assert.Contains("typeof(TunnelPage)", managedPageTypes);
        Assert.Contains("typeof(PortsMapPage)", managedPageTypes);
        Assert.Contains("typeof(RemoteControlPage)", managedPageTypes);
        Assert.Contains("typeof(JavaSetupPage)", managedPageTypes);
        Assert.Contains("typeof(AboutPage)", managedPageTypes);
        Assert.Contains("typeof(AppSettingsPage)", managedPageTypes);
        Assert.DoesNotContain("ServerSettingsPage", managedPageTypes);
        Assert.Contains("ScrollViewerHelper.DisableAncestorScrollViewers(page)", source);
    }

    [Fact]
    public void ScrollViewerHelper_UsesDetachablePreviewWheelForwarding()
    {
        string source = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Infrastructure",
            "ScrollViewerHelper.cs"));

        Assert.Contains("MouseWheelHandlerProperty", source);
        Assert.Contains("UIElement.PreviewMouseWheelEvent", source);
        Assert.Contains("RemoveHandler(UIElement.PreviewMouseWheelEvent", source);
        Assert.Contains("ShouldSkipWheelForwarding", source);
        Assert.Contains("FindAncestor<ScrollBar>", source);
        Assert.Contains("FindAncestor<Popup>", source);
        Assert.Contains("FindAncestor<ComboBox>", source);
        Assert.Contains("FindAncestor<TextBox>", source);
    }

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

        Assert.True(start >= 0, $"Start marker '{startMarker}' was not found.");
        Assert.True(end > start, $"End marker '{endMarker}' was not found after '{startMarker}'.");

        return source[start..end];
    }

    private static string ReadFeatureFile(string relativeFeaturePath)
    {
        string[] path = relativeFeaturePath.Split('/');
        return File.ReadAllText(TestSourceFileResolver.Resolve(
            new[] { "PocketMC.Desktop", "Features" }.Concat(path).ToArray()));
    }
}
