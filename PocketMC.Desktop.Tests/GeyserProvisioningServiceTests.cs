using System.IO;
using PocketMC.Desktop.Features.Instances.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace PocketMC.Desktop.Tests;

public sealed class GeyserProvisioningServiceTests
{
    [Fact]
    public void PatchGeyserConfigPort_WithExistingConfig_UpdatesPort()
    {
        using var workspace = new PortReliabilityTestWorkspace();
        var service = new GeyserProvisioningService(null!, null!, NullLogger<GeyserProvisioningService>.Instance);
        var metadata = workspace.CreateInstance("Geyser Patch", serverType: "Paper");

        string configPath = Path.Combine(workspace.GetInstancePath(metadata.Id), "plugins", "Geyser-Spigot");
        Directory.CreateDirectory(configPath);
        string file = Path.Combine(configPath, "config.yml");
        File.WriteAllText(file, "bedrock:\n  address: 0.0.0.0\n  port: 19132\n  clone-remote-port: false\n");

        service.PatchGeyserConfigPort(workspace.GetInstancePath(metadata.Id), 19150);

        string patched = File.ReadAllText(file);
        Assert.Contains("port: 19150", patched);
        Assert.DoesNotContain("port: 19132", patched);
    }
}
