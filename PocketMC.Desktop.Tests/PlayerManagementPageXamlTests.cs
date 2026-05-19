namespace PocketMC.Desktop.Tests;

public sealed class PlayerManagementPageXamlTests
{
    [Fact]
    public void PlayerLists_StretchRowsToKeepColumnsAligned()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Players",
            "PlayerManagementPage.xaml"));

        Assert.Equal(2, CountOccurrences(xaml, "<ListView.ItemContainerStyle>"));
        Assert.Equal(2, CountOccurrences(xaml, "Property=\"HorizontalContentAlignment\" Value=\"Stretch\""));
        Assert.Equal(2, CountOccurrences(xaml, "<ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\"/>"));
    }

    private static int CountOccurrences(string value, string expected)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }

        return count;
    }
}
