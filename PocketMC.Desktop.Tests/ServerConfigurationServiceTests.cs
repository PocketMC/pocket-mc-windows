using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Tests;

public sealed class ServerConfigurationServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_SeparatesCoreAndAdvancedServerProperties()
    {
        var manager = CreateManager(out var registry, out _);
        var service = new ServerConfigurationService(manager, registry);
        var metadata = manager.CreateInstance("Settings Test", "");
        string serverDir = registry.GetPath(metadata.Id)!;
        File.WriteAllLines(
            Path.Combine(serverDir, "server.properties"),
            new[] { "motd=Hello", "max-players=12", "view-distance=9" },
            new UTF8Encoding(false));

        var configuration = service.Load(metadata, serverDir);

        Assert.Equal("Hello", configuration.Motd);
        Assert.Equal("12", configuration.MaxPlayers);
        Assert.Equal("9", configuration.AdvancedProperties["view-distance"]);
        Assert.Equal("Hello", configuration.AllProperties["motd"]);
        Assert.Equal("12", configuration.AllProperties["max-players"]);
        Assert.Equal("9", configuration.AllProperties["view-distance"]);
        Assert.False(configuration.AdvancedProperties.ContainsKey("motd"));
    }

    [Fact]
    public void Save_UpdatesMetadataAndServerProperties()
    {
        var manager = CreateManager(out var registry, out _);
        var service = new ServerConfigurationService(manager, registry);
        var metadata = manager.CreateInstance("Settings Save Test", "");
        string serverDir = registry.GetPath(metadata.Id)!;
        File.WriteAllText(Path.Combine(serverDir, "server.properties"), "motd=Old" + Environment.NewLine, new UTF8Encoding(false));

        var configuration = new ServerConfiguration
        {
            MinRamMb = 2048,
            MaxRamMb = 6144,
            Motd = "New",
            MaxPlayers = "30",
            ServerPort = "25566",
            SpawnProtection = "8",
            LevelType = "minecraft:flat",
            Gamemode = "creative",
            Difficulty = "hard",
            Pvp = true,
            AllowNether = true
        };
        configuration.AdvancedProperties["view-distance"] = "10";

        service.Save(metadata, serverDir, configuration);

        var props = ServerPropertiesParser.Read(Path.Combine(serverDir, "server.properties"));
        Assert.Equal("New", props["motd"]);
        Assert.Equal("30", props["max-players"]);
        Assert.Equal("10", props["view-distance"]);

        var metadataJson = File.ReadAllText(Path.Combine(serverDir, ".pocket-mc.json"));
        var savedMetadata = JsonSerializer.Deserialize<InstanceMetadata>(metadataJson)!;
        Assert.Equal(2048, savedMetadata.MinRamMb);
        Assert.Equal(6144, savedMetadata.MaxRamMb);
    }

    private InstanceManager CreateManager(out InstanceRegistry registry, out InstancePathService pathService)
    {
        var state = new ApplicationState();
        state.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });

        pathService = new InstancePathService(state);
        registry = new InstanceRegistry(pathService, NullLogger<InstanceRegistry>.Instance);

        return new InstanceManager(registry, pathService, state, NullLogger<InstanceManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            foreach (var file in Directory.GetFiles(_tempDirectory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
