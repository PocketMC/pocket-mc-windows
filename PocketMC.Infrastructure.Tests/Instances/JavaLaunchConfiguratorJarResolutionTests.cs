using System;
using System.IO;
using PocketMC.Infrastructure.Instances;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances;

public sealed class JavaLaunchConfiguratorJarResolutionTests : IDisposable
{
    private readonly string _tempDirectory;

    public JavaLaunchConfiguratorJarResolutionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC_JarResolution_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_tempDirectory, name), "fake");

    [Fact]
    public void ResolveStartableJar_PrefersServerJar()
    {
        Touch("server.jar");
        Touch("mohist-1.20.1-101-server.jar");

        Assert.Equal("server.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_UsesMohistJarWhenServerJarMissing()
    {
        Touch("mohist-1.20.1-101-server.jar");

        Assert.Equal("mohist-1.20.1-101-server.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_IgnoresInstallerJars()
    {
        Touch("forge-1.20.1-47.2.0-installer.jar");
        Touch("arclight-forge-1.20.1-1.0.2.jar");

        Assert.Equal("arclight-forge-1.20.1-1.0.2.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_PrefersForgeJarForForgeInstalls()
    {
        Touch("server.jar");
        Touch("forge-1.20.1-47.2.0-universal.jar");

        Assert.Equal("forge-1.20.1-47.2.0-universal.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: true));
    }

    [Fact]
    public void ResolveStartableJar_ReturnsSingleUnknownJar()
    {
        Touch("custom-server-build.jar");

        Assert.Equal("custom-server-build.jar", JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }

    [Fact]
    public void ResolveStartableJar_ReturnsNullWhenAmbiguous()
    {
        Touch("alpha.jar");
        Touch("beta.jar");

        Assert.Null(JavaLaunchConfigurator.ResolveStartableJar(_tempDirectory, preferForgeJar: false));
    }
}
