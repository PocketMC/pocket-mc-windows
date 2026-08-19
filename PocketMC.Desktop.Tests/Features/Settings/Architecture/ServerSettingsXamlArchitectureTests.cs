using PocketMC.Desktop.Tests.TestSupport.Utilities;
namespace PocketMC.Desktop.Tests.Features.Settings.Architecture;

public sealed class ServerSettingsXamlArchitectureTests
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

    [Fact]
    public void VersionUpdates_TargetVersionUsesDropdownInsteadOfFreeformText()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Settings",
            "ServerSettingsPage.xaml"));

        Assert.DoesNotContain("Text=\"{Binding VersionUpdates.TargetMinecraftVersion", xaml);
        Assert.Contains("ItemsSource=\"{Binding VersionUpdates.TargetVersions}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding VersionUpdates.SelectedTargetVersion", xaml);
    }

    [Fact]
    public void VersionUpdates_ActionsAndProgressRenderInBottomActionBar()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Settings",
            "ServerSettingsPage.xaml"));

        int bottomActionBarIndex = xaml.IndexOf("<!-- Bottom Action Bar -->", StringComparison.Ordinal);
        int mainLayoutIndex = xaml.IndexOf("<!-- Main Layout Grid -->", StringComparison.Ordinal);
        int applyCommandIndex = xaml.IndexOf("Command=\"{Binding VersionUpdates.ApplyMinecraftUpdateCommand}\"", StringComparison.Ordinal);
        int progressIndex = xaml.IndexOf("Value=\"{Binding VersionUpdates.UpdateProgressValue}\"", StringComparison.Ordinal);

        // ApplyCommand is now inside the Version Updates card (after Main Layout)
        Assert.True(applyCommandIndex > mainLayoutIndex, "ApplyCommand should be in the Version Updates tab content");
        // Progress bar should be in the bottom action bar
        Assert.InRange(progressIndex, bottomActionBarIndex, mainLayoutIndex);
    }
}
