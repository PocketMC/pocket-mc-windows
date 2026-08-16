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
}
