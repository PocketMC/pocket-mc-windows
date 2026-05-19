namespace PocketMC.Desktop.Tests;

public sealed class ServerSettingsPageXamlTests
{
    [Fact]
    public void DefaultServerPortTextBindings_AreOneWayBecausePropertyIsReadOnly()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Settings",
            "ServerSettingsPage.xaml"));

        Assert.DoesNotContain("{Binding DefaultServerPortText}", xaml);
        Assert.Contains("{Binding DefaultServerPortText, Mode=OneWay}", xaml);
    }
}
