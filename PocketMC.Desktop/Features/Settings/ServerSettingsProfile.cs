using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Features.Settings;

public sealed class ServerSettingsProfile
{
    private ServerSettingsProfile(
        EngineFamily family,
        bool hasGeyser)
    {
        Family = family;
        IsJava = family is EngineFamily.Vanilla or EngineFamily.Spigot or EngineFamily.Fabric or EngineFamily.Forge or EngineFamily.NeoForge;
        IsBedrockDedicated = family == EngineFamily.Bedrock;
        IsPocketMine = family == EngineFamily.Pocketmine;
        HasGeyser = hasGeyser;
    }

    public EngineFamily Family { get; }
    public bool IsJava { get; }
    public bool IsBedrockDedicated { get; }
    public bool IsPocketMine { get; }
    public bool HasGeyser { get; }

    public bool SupportsJavaRuntimeSettings => IsJava;
    public bool SupportsJavaWorldGenerator => IsJava;
    public bool SupportsNether => IsJava;
    public bool SupportsCommandBlocks => IsJava;
    public bool SupportsAllowFlight => IsJava || IsPocketMine;
    public bool SupportsSpawnProtection => IsJava || IsPocketMine;
    public bool SupportsBedrockRules => IsBedrockDedicated || IsPocketMine;
    public bool SupportsBedrockWorlds => IsBedrockDedicated || IsPocketMine;
    public bool SupportsGeyserSettings => IsJava && HasGeyser;

    public string DefaultServerPort => IsJava ? "25565" : "19132";
    public string DisplayNamePropertyKey => IsJava ? "motd" : "server-name";
    public string DisplayNameLabel => IsJava ? "MOTD" : "Server Name";
    public string DefaultLevelType => IsJava ? "minecraft:normal" : string.Empty;

    public string[] LevelTypes => IsJava
        ? new[] { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "minecraft:single_biome_surface" }
        : new[] { "DEFAULT", "FLAT" };

    public string[] Gamemodes => IsJava
        ? new[] { "survival", "creative", "adventure", "spectator" }
        : new[] { "survival", "creative", "adventure" };

    public string[] Difficulties => new[] { "peaceful", "easy", "normal", "hard" };

    public string[] BedrockPermissionLevels => new[] { "visitor", "member", "operator" };

    public static ServerSettingsProfile FromMetadata(InstanceMetadata metadata, string serverDir, PocketMC.Desktop.Helpers.IGeyserDetector geyserDetector)
    {
        return new ServerSettingsProfile(metadata.Compatibility.Family, geyserDetector.IsGeyserInstalled(serverDir));
    }
}
