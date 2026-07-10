namespace PocketMC.Desktop.Tests;

public sealed class DashboardPageXamlTests
{
    [Fact]
    public void InstanceCardMetadata_UsesLabeledFieldsInsteadOfDenseInlineText()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Dashboard",
            "DashboardPage.xaml"));

        string metadataXaml = ExtractMetadataSection(xaml);

        Assert.Contains("MetadataItemLabel", metadataXaml);
        Assert.Contains("Text=\"{Binding LastPlayedLabelText}\"", metadataXaml);
        Assert.Contains("{Binding LastPlayedValueText}", metadataXaml);
        Assert.DoesNotContain("{Binding LastPlayedText}\" FontSize=\"11\"", metadataXaml);
    }

    [Fact]
    public void InstanceCardMetadata_DoesNotRepeatMetricFields()
    {
        string xaml = File.ReadAllText(TestSourceFileResolver.Resolve(
            "PocketMC.Desktop",
            "Features",
            "Dashboard",
            "DashboardPage.xaml"));

        string metadataXaml = ExtractMetadataSection(xaml);

        Assert.DoesNotContain("Text=\"RAM\"", metadataXaml);
        Assert.DoesNotContain("Text=\"Slots\"", metadataXaml);
        Assert.DoesNotContain("{Binding MemoryValueText}", metadataXaml);
        Assert.DoesNotContain("{Binding PlayerLimitValueText}", metadataXaml);
    }

    private static string ExtractMetadataSection(string xaml)
    {
        int start = xaml.LastIndexOf("<!-- Metadata -->", StringComparison.Ordinal);
        int end = xaml.IndexOf("<!-- Metrics -->", start, StringComparison.Ordinal);

        Assert.True(start >= 0, "Metadata section marker was not found.");
        Assert.True(end > start, "Metrics section marker was not found after metadata.");

        return xaml[start..end];
    }
}
