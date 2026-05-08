using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances;

namespace PocketMC.Desktop.Tests;

public sealed class ServerConfigurationServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_SeparatesCoreAndAdvancedServerProperties()
    {
        var manager = CreateManager(out var registry, out _);
        var service = new ServerConfigurationService(manager);
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
        var service = new ServerConfigurationService(manager);
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

    [Fact]
    public void Load_BedrockUsesServerNameAndBedrockDefaults()
    {
        var manager = CreateManager(out var registry, out _);
        var service = new ServerConfigurationService(manager);
        var metadata = manager.CreateInstance("Bedrock Settings Test", "", "Bedrock Dedicated Server");
        string serverDir = registry.GetPath(metadata.Id)!;
        File.WriteAllLines(
            Path.Combine(serverDir, "server.properties"),
            new[]
            {
                "server-name=Bedrock Realm",
                "server-port=19132",
                "server-portv6=19133",
                "allow-cheats=true",
                "texturepack-required=true",
                "default-player-permission-level=operator",
                "tick-distance=8"
            },
            new UTF8Encoding(false));

        var configuration = service.Load(metadata, serverDir);

        Assert.Equal("Bedrock Realm", configuration.Motd);
        Assert.Equal("19132", configuration.ServerPort);
        Assert.Equal("19133", configuration.ServerPortV6);
        Assert.True(configuration.AllowCheats);
        Assert.True(configuration.TexturepackRequired);
        Assert.Equal("operator", configuration.DefaultPlayerPermissionLevel);
        Assert.Equal("8", configuration.TickDistance);
    }

    [Fact]
    public void Save_BedrockWritesBedrockPropertiesAndSkipsJavaOnlyKeys()
    {
        var manager = CreateManager(out var registry, out _);
        var service = new ServerConfigurationService(manager);
        var metadata = manager.CreateInstance("Bedrock Save Test", "", "Bedrock Dedicated Server");
        string serverDir = registry.GetPath(metadata.Id)!;
        File.WriteAllText(Path.Combine(serverDir, "server.properties"), "level-type=minecraft:normal" + Environment.NewLine, new UTF8Encoding(false));

        var configuration = new ServerConfiguration
        {
            Motd = "Bedrock Name",
            MaxPlayers = "12",
            ServerPort = "19132",
            ServerPortV6 = "19134",
            Gamemode = "creative",
            Difficulty = "normal",
            OnlineMode = true,
            Pvp = true,
            WhiteList = false,
            AllowCheats = true,
            TexturepackRequired = true,
            DefaultPlayerPermissionLevel = "member",
            TickDistance = "6"
        };

        service.Save(metadata, serverDir, configuration);

        var props = ServerPropertiesParser.Read(Path.Combine(serverDir, "server.properties"));
        Assert.Equal("Bedrock Name", props["server-name"]);
        Assert.Equal("19134", props["server-portv6"]);
        Assert.Equal("true", props["allow-cheats"]);
        Assert.Equal("true", props["texturepack-required"]);
        Assert.Equal("member", props["default-player-permission-level"]);
        Assert.Equal("6", props["tick-distance"]);
        Assert.False(props.ContainsKey("motd"));
        Assert.False(props.ContainsKey("level-type"));
        Assert.False(props.ContainsKey("allow-nether"));
        Assert.False(props.ContainsKey("enable-command-block"));
    }

    [Fact]
    public void Save_JavaWritesJavaPropertiesAndSkipsBedrockOnlyKeys()
    {
        var manager = CreateManager(out var registry, out _);
        var service = new ServerConfigurationService(manager);
        var metadata = manager.CreateInstance("Java Save Test", "", "Paper");
        string serverDir = registry.GetPath(metadata.Id)!;
        File.WriteAllText(Path.Combine(serverDir, "server.properties"), "server-name=Old Bedrock Name" + Environment.NewLine, new UTF8Encoding(false));

        var configuration = new ServerConfiguration
        {
            Motd = "Java MOTD",
            MaxPlayers = "20",
            ServerPort = "25565",
            SpawnProtection = "16",
            LevelType = "minecraft:flat",
            Gamemode = "survival",
            Difficulty = "easy",
            Pvp = true,
            AllowNether = true,
            AllowFlight = false,
            AllowCommandBlock = false,
            AllowCheats = true,
            TexturepackRequired = true
        };

        service.Save(metadata, serverDir, configuration);

        var props = ServerPropertiesParser.Read(Path.Combine(serverDir, "server.properties"));
        Assert.Equal("Java MOTD", props["motd"]);
        Assert.Equal("minecraft:flat", props["level-type"]);
        Assert.Equal("true", props["allow-nether"]);
        Assert.False(props.ContainsKey("server-name"));
        Assert.False(props.ContainsKey("allow-cheats"));
        Assert.False(props.ContainsKey("texturepack-required"));
    }

    private sealed class MockAssetProvider : PocketMC.Desktop.Core.Interfaces.IAssetProvider
    {
        public Stream? GetAssetStream(string assetName) => null;
    }

    private InstanceManager CreateManager(out InstanceRegistry registry, out InstancePathService pathService)
    {
        var state = new ApplicationState();
        state.ApplySettings(new AppSettings { AppRootPath = _tempDirectory });

        pathService = new InstancePathService(state);
        registry = new InstanceRegistry(pathService, NullLogger<InstanceRegistry>.Instance);

        return new InstanceManager(registry, pathService, state, new MockAssetProvider(), NullLogger<InstanceManager>.Instance);
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
