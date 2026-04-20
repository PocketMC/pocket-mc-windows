using Moq;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Providers;
using PocketMC.Desktop.Features.Java;
using PocketMC.Desktop.Models;
using System.Diagnostics;

namespace PocketMC.Desktop.Tests;

public sealed class ServerLaunchConfiguratorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));
    private readonly Mock<JavaProvisioningService> _javaMock;
    private readonly Mock<PhpProvisioningService> _phpMock;
    private readonly Mock<VanillaProvider> _vanillaMock;
    private readonly Mock<ILogger<ServerLaunchConfigurator>> _loggerMock;
    private readonly ServerLaunchConfigurator _configurator;

    public ServerLaunchConfiguratorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        
        // Use a realish but inert provisioner setup if needed, or just mock the class if virtual methods allow.
        // Actually JavaProvisioningService methods aren't virtual. I might need to mock dependencies instead.
        // For simplicity in this test, I'll mock the dependencies of the provisioners if I can't mock them directly.
        // But ServerLaunchConfigurator only calls Ensured versions which we can assume pass for these unit tests.
        
        _javaMock = new Mock<JavaProvisioningService>(null!, null!, null!, null!, null!);
        _javaMock.Setup(x => x.IsJavaVersionPresent(It.IsAny<int>())).Returns(true);
        
        _phpMock = new Mock<PhpProvisioningService>(new HttpClient(), null!, null!, null!);
        _vanillaMock = new Mock<VanillaProvider>(new HttpClient(), null!, null!, null!);
        _loggerMock = new Mock<ILogger<ServerLaunchConfigurator>>();

        _configurator = new ServerLaunchConfigurator(_javaMock.Object, _phpMock.Object, _vanillaMock.Object, _loggerMock.Object);
    }

    [Theory]
    [InlineData("1.8.8", true)]
    [InlineData("1.12.2", true)]
    [InlineData("1.16.5", true)]
    [InlineData("1.17.1", true)]
    [InlineData("1.18", true)]
    [InlineData("1.18.1", false)]
    [InlineData("1.21.1", false)]
    public async Task ConfigureAsync_InjectsLog4jMitigation_WhenVersionIsVulnerable(string mcVersion, bool expectMitigation)
    {
        var meta = new InstanceMetadata { MinecraftVersion = mcVersion, ServerType = "Vanilla", CustomJavaPath = Path.Combine(_tempDirectory, "java.exe") };
        File.WriteAllText(meta.CustomJavaPath, "");
        File.WriteAllText(Path.Combine(_tempDirectory, "server.jar"), "");

        var psi = await _configurator.ConfigureAsync(meta, _tempDirectory, _tempDirectory, _ => { });

        Assert.Equal(expectMitigation, psi.ArgumentList.Contains("-Dlog4j2.formatMsgNoLookups=true"));
    }

    [Theory]
    [InlineData("1.16.5", false)]
    [InlineData("1.17", true)]
    [InlineData("1.21", true)]
    public async Task ConfigureAsync_AlwaysPreTouch_OnlyForModernVersions(string mcVersion, bool expectPreTouch)
    {
        var meta = new InstanceMetadata { MinecraftVersion = mcVersion, ServerType = "Vanilla", CustomJavaPath = Path.Combine(_tempDirectory, "java.exe") };
        File.WriteAllText(meta.CustomJavaPath, "");
        File.WriteAllText(Path.Combine(_tempDirectory, "server.jar"), "");

        var psi = await _configurator.ConfigureAsync(meta, _tempDirectory, _tempDirectory, _ => { });

        Assert.Equal(expectPreTouch, psi.ArgumentList.Contains("-XX:+AlwaysPreTouch"));
    }

    [Fact]
    public async Task ConfigureAsync_ThrowsFileNotFound_WhenJarMissing()
    {
        var meta = new InstanceMetadata { MinecraftVersion = "1.21", ServerType = "Vanilla", CustomJavaPath = Path.Combine(_tempDirectory, "java.exe") };
        File.WriteAllText(meta.CustomJavaPath, "");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _configurator.ConfigureAsync(meta, _tempDirectory, _tempDirectory, _ => { }));
    }

    [Fact]
    public async Task ConfigureAsync_DetectsForgeJar_WithoutUniversalSuffix()
    {
        var meta = new InstanceMetadata { MinecraftVersion = "1.12.2", ServerType = "Forge", CustomJavaPath = Path.Combine(_tempDirectory, "java.exe") };
        File.WriteAllText(meta.CustomJavaPath, "");
        
        string forgeJar = Path.Combine(_tempDirectory, "forge-1.12.2-14.23.5.2859.jar");
        File.WriteAllText(forgeJar, "");

        var psi = await _configurator.ConfigureAsync(meta, _tempDirectory, _tempDirectory, _ => { });

        Assert.Contains(Path.GetFileName(forgeJar), psi.ArgumentList);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
