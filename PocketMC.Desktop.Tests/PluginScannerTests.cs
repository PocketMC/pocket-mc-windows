using System.IO.Compression;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Features.Mods;

namespace PocketMC.Desktop.Tests;

public sealed class PluginScannerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryGetPluginMetadata_ReadsPluginNameAndApiVersion()
    {
        Directory.CreateDirectory(_tempDirectory);
        string jarPath = Path.Combine(_tempDirectory, "example.jar");

        using (var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("plugin.yml");
            using var writer = new StreamWriter(entry.Open());
            writer.WriteLine("name: ExamplePlugin");
            writer.WriteLine("api-version: '1.20'");
        }

        Assert.Equal("ExamplePlugin", PluginScanner.TryGetPluginName(jarPath));
        Assert.Equal("1.20", PluginScanner.TryGetApiVersion(jarPath));
    }

    [Theory]
    [InlineData("1.21", "1.20.4", true)]
    [InlineData("1.14", "1.20.4", false)]
    [InlineData("1.20", "1.20.4", false)]
    [InlineData(null, "1.20.4", false)]
    public void IsIncompatible_RespectsBackwardCompatibility(string? pluginVersion, string serverVersion, bool expected)
    {
        Assert.Equal(expected, PluginScanner.IsIncompatible(pluginVersion, serverVersion));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
