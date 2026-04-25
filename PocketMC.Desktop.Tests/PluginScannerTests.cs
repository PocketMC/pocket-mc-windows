using System.IO.Compression;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;
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

#pragma warning disable CS0618
        Assert.Equal("ExamplePlugin", PluginScanner.TryGetPluginName(jarPath));
        Assert.Equal("1.20", PluginScanner.TryGetApiVersion(jarPath));
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData("1.21", "1.20.4", true)]
    [InlineData("1.14", "1.20.4", false)]
    [InlineData("1.20", "1.20.4", false)]
    [InlineData(null, "1.20.4", false)]
    public void IsIncompatible_RespectsBackwardCompatibility(string? pluginVersion, string serverVersion, bool expected)
    {
#pragma warning disable CS0618
        Assert.Equal(expected, PluginScanner.IsIncompatible(pluginVersion, serverVersion));
#pragma warning restore CS0618
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
