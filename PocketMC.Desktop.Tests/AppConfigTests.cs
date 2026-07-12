using System.IO;
using System.Text.RegularExpressions;
using PocketMC.Infrastructure.Telemetry;

namespace PocketMC.Desktop.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void AppVersion_LoadsFromEmbeddedPocketMcConfig()
    {
        var desktopAssembly = typeof(PocketMC.Desktop.App).Assembly;
        using var stream = desktopAssembly.GetManifestResourceStream("PocketMC.Desktop.pocketmc.yml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        var versionMatch = Regex.Match(content, @"(?m)^version:\s*""?([^""\r\n]+)""?");

        Assert.True(versionMatch.Success);
        Assert.Equal(versionMatch.Groups[1].Value, AppConfig.AppVersion);
    }
}
