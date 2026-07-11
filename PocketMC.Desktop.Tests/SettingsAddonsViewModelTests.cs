using System.Collections.Generic;
using PocketMC.Desktop.Features.Settings;
using Xunit;

namespace PocketMC.Desktop.Tests;

public sealed class SettingsAddonsVMTests
{
    [Fact]
    public void FormatAddonUpdateWarningText_ReturnsEmpty_ForEmptyOrNull()
    {
        Assert.Equal("", SettingsAddonsVM.FormatAddonUpdateWarningText(null!));
        Assert.Equal("", SettingsAddonsVM.FormatAddonUpdateWarningText(new List<string>()));
    }

    [Fact]
    public void FormatAddonUpdateWarningText_FormatsListCorrectly()
    {
        var warnings = new List<string> { "Beta release", "Requires manual check" };
        string formatted = SettingsAddonsVM.FormatAddonUpdateWarningText(warnings);
        Assert.Contains("Warnings:", formatted);
        Assert.Contains("• Beta release", formatted);
        Assert.Contains("• Requires manual check", formatted);
    }

    [Fact]
    public void BuildUpdateConfirmationMessage_AppendsWarnings()
    {
        var warnings = new List<string> { "Warning 1" };
        string message = SettingsAddonsVM.BuildUpdateConfirmationMessage("MyMod", "1.0", "2.0", warnings);
        Assert.Contains("MyMod", message);
        Assert.Contains("Installed: 1.0", message);
        Assert.Contains("Latest: 2.0", message);
        Assert.Contains("Warnings:\n• Warning 1", message);
    }

    [Fact]
    public void BuildReinstallConfirmationMessage_AppendsWarnings()
    {
        var warnings = new List<string> { "Warning 1" };
        string message = SettingsAddonsVM.BuildReinstallConfirmationMessage("MyMod", "2.0", warnings);
        Assert.Contains("MyMod", message);
        Assert.Contains("reinstall", message);
        Assert.Contains("Warnings:\n• Warning 1", message);
    }

    [Fact]
    public void BuildBatchUpdateSummaryMessage_AppendsAllWarnings()
    {
        var updates = new List<(string Name, string LatestVersionName)>
        {
            ("ModA", "2.0"),
            ("ModB", "3.0")
        };
        var warnings = new List<string> { "Warning A", "Warning B" };
        string message = SettingsAddonsVM.BuildBatchUpdateSummaryMessage(2, 5, updates, warnings);
        Assert.Contains("2 of 5 addon(s)", message);
        Assert.Contains("• ModA  →  2.0", message);
        Assert.Contains("• ModB  →  3.0", message);
        Assert.Contains("Warnings:\n• Warning A", message);
        Assert.Contains("• Warning B", message);
    }
}
