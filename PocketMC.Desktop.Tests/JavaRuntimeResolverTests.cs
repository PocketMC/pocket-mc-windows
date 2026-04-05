using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Tests;

public sealed class JavaRuntimeResolverTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("1.8.9", 8)]
    [InlineData("1.16.4", 8)]
    [InlineData("1.16.5", 11)]
    [InlineData("1.17.1", 11)]
    [InlineData("1.18", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.21.1", 21)]
    [InlineData("1.20.5-pre1", 21)]
    [InlineData("snapshot", 21)]
    public void GetRequiredJavaVersion_MapsMinecraftVersionsToExpectedRuntimes(string minecraftVersion, int expectedJavaVersion)
    {
        Assert.Equal(expectedJavaVersion, JavaRuntimeResolver.GetRequiredJavaVersion(minecraftVersion));
    }

    [Fact]
    public void ResolveJavaPath_PrefersCustomJavaPath_WhenItExists()
    {
        Directory.CreateDirectory(_tempDirectory);
        string customJavaPath = Path.Combine(_tempDirectory, "custom-java.exe");
        File.WriteAllText(customJavaPath, string.Empty);

        var metadata = new InstanceMetadata
        {
            MinecraftVersion = "1.21.1",
            CustomJavaPath = customJavaPath
        };

        Assert.Equal(customJavaPath, JavaRuntimeResolver.ResolveJavaPath(metadata, _tempDirectory));
    }

    [Fact]
    public void ResolveJavaPath_UsesBundledRuntime_WhenNoCustomPathIsSet()
    {
        string bundledJavaPath = Path.Combine(_tempDirectory, "runtime", "java17", "bin", "java.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(bundledJavaPath)!);
        File.WriteAllText(bundledJavaPath, string.Empty);

        var metadata = new InstanceMetadata
        {
            MinecraftVersion = "1.20.4"
        };

        Assert.Equal(bundledJavaPath, JavaRuntimeResolver.ResolveJavaPath(metadata, _tempDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
