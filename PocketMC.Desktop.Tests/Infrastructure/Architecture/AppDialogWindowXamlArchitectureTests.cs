using PocketMC.Desktop.Tests.TestSupport.Utilities;
namespace PocketMC.Desktop.Tests.Infrastructure.Architecture;

public sealed class AppDialogWindowXamlArchitectureTests
{
    [Fact]
    public void DialogWindow_OverridesFluentWindowDefaultMinimumHeight()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Infrastructure",
            "AppDialogWindow.xaml"));

        Assert.Contains("MinHeight=\"0\"", xaml);
        Assert.DoesNotContain("<RowDefinition Height=\"*\"/>", xaml);
    }

    [Fact]
    public void WhatsNewWindow_DefinesProperDimensionsAndTextWrapping()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "WhatsNew",
            "WhatsNewWindow.xaml"));

        Assert.Contains("Width=\"580\"", xaml);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", xaml);
        Assert.Contains("Foreground=\"{DynamicResource AccentTextFillColorPrimaryBrush}\"", xaml);
        Assert.Contains("x:Name=\"BtnFullChangelog\"", xaml);
        Assert.Contains("x:Name=\"SectionsPanel\"", xaml);
    }
}
