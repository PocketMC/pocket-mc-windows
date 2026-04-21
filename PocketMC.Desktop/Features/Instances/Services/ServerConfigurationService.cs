using PocketMC.Desktop.Features.Instances.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;

namespace PocketMC.Desktop.Features.Instances.Services;

public sealed class ServerConfigurationService
{
    private const string ServerPropertiesFileName = "server.properties";

    private static readonly HashSet<string> CorePropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Java / shared ─────────────────────────────────────────────────
        "motd",
        "level-seed",
        "spawn-protection",
        "max-players",
        "server-port",
        "server-ip",
        "level-type",
        "online-mode",
        "pvp",
        "white-list",
        "gamemode",
        "difficulty",
        "enable-command-block",
        "allow-flight",
        "allow-nether",

        // ── Bedrock Dedicated Server (BDS) ────────────────────────────────
        "server-portv6",            // IPv6 listen port
        "allow-cheats",             // op-level cheat commands
        "texturepack-required",     // enforce resource pack on join
        "default-player-permission-level", // visitor / member / operator
        "tick-distance",            // simulation radius (chunk ticks)
        "emit-server-telemetry",    // MS telemetry toggle

        // ── PocketMine-MP ─────────────────────────────────────────────────
        "server-name",              // PM uses this instead of motd
        "enable-query",
        "auto-save",
        "language",
    };

    private readonly InstanceManager _instanceManager;

    public ServerConfigurationService(InstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }

    public ServerConfiguration Load(InstanceMetadata metadata, string serverDir)
    {
        var props = ServerPropertiesParser.Read(GetPropertiesPath(serverDir));

        // Sync metadata if needed (NET-15)
        if (props.TryGetValue("motd", out var pMotd)) metadata.Motd = pMotd;
        if (props.TryGetValue("max-players", out var pMax) && int.TryParse(pMax, out int max)) metadata.MaxPlayers = max;

        bool isBedrock = metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true || 
                         metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;
        string defaultPort = isBedrock ? "19132" : "25565";

        var configuration = new ServerConfiguration
        {
            MinRamMb = metadata.MinRamMb > 0 ? metadata.MinRamMb : 1024,
            MaxRamMb = metadata.MaxRamMb > 0 ? metadata.MaxRamMb : 4096,
            CustomJavaPath = metadata.CustomJavaPath,
            AdvancedJvmArgs = metadata.AdvancedJvmArgs,
            EnableAutoRestart = metadata.EnableAutoRestart,
            MaxAutoRestarts = metadata.MaxAutoRestarts,
            AutoRestartDelaySeconds = metadata.AutoRestartDelaySeconds,
            BackupIntervalHours = metadata.BackupIntervalHours,
            MaxBackupsToKeep = metadata.MaxBackupsToKeep,
            Motd = props.TryGetValue("motd", out var motd) ? motd : "A Minecraft Server",
            Seed = props.TryGetValue("level-seed", out var seed) ? seed : "",
            SpawnProtection = props.TryGetValue("spawn-protection", out var protection) ? protection : "16",
            MaxPlayers = props.TryGetValue("max-players", out var maxPlayers) ? maxPlayers : "20",
            ServerPort = props.TryGetValue("server-port", out var port) ? port : defaultPort,
            ServerIp = props.TryGetValue("server-ip", out var ip) ? ip : "",
            LevelType = props.TryGetValue("level-type", out var levelType) ? levelType : "minecraft:normal",
            OnlineMode = props.TryGetValue("online-mode", out var onlineMode) && onlineMode == "true",
            Pvp = props.TryGetValue("pvp", out var pvp) ? pvp == "true" : true,
            WhiteList = props.TryGetValue("white-list", out var whiteList) && whiteList == "true",
            Gamemode = props.TryGetValue("gamemode", out var gamemode) ? gamemode : "survival",
            Difficulty = props.TryGetValue("difficulty", out var difficulty) ? difficulty : "easy",
            AllowCommandBlock = props.TryGetValue("enable-command-block", out var commandBlock) && commandBlock == "true",
            AllowFlight = props.TryGetValue("allow-flight", out var allowFlight) && allowFlight == "true",
            AllowNether = props.TryGetValue("allow-nether", out var allowNether) ? allowNether == "true" : true
        };

        foreach (var property in props)
        {
            configuration.AllProperties[property.Key] = property.Value;
        }

        foreach (var property in props.Where(property => !CorePropertyKeys.Contains(property.Key)))
        {
            configuration.AdvancedProperties[property.Key] = property.Value;
        }

        return configuration;
    }

    public void Save(InstanceMetadata metadata, string serverDir, ServerConfiguration configuration)
    {
        metadata.MinRamMb = configuration.MinRamMb;
        metadata.MaxRamMb = configuration.MaxRamMb;
        metadata.EnableAutoRestart = configuration.EnableAutoRestart;
        metadata.MaxAutoRestarts = configuration.MaxAutoRestarts;
        metadata.AutoRestartDelaySeconds = configuration.AutoRestartDelaySeconds;
        metadata.BackupIntervalHours = configuration.BackupIntervalHours;
        metadata.MaxBackupsToKeep = configuration.MaxBackupsToKeep;
        metadata.CustomJavaPath = string.IsNullOrWhiteSpace(configuration.CustomJavaPath) ? null : configuration.CustomJavaPath;
        metadata.AdvancedJvmArgs = string.IsNullOrWhiteSpace(configuration.AdvancedJvmArgs) ? null : configuration.AdvancedJvmArgs.Trim();
        metadata.Motd = configuration.Motd;
        if (int.TryParse(configuration.MaxPlayers, out int mp)) metadata.MaxPlayers = mp;

        _instanceManager.SaveMetadata(metadata, serverDir);

        var propsFile = GetPropertiesPath(serverDir);
        var props = ServerPropertiesParser.Read(propsFile);

        props["motd"] = configuration.Motd;
        if (!string.IsNullOrWhiteSpace(configuration.Seed))
        {
            props["level-seed"] = configuration.Seed;
        }

        props["spawn-protection"] = configuration.SpawnProtection;
        props["max-players"] = configuration.MaxPlayers;
        props["server-port"] = configuration.ServerPort;

        bool isBedrock = metadata.ServerType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true || 
                         metadata.ServerType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true;
        
        if (isBedrock && int.TryParse(configuration.ServerPort, out int parsedPort))
        {
            props["server-portv6"] = (parsedPort + 1).ToString();
        }

        if (!string.IsNullOrWhiteSpace(configuration.ServerIp))
        {
            props["server-ip"] = configuration.ServerIp;
        }
        else
        {
            props.Remove("server-ip");
        }

        props["level-type"] = configuration.LevelType;
        props["online-mode"] = configuration.OnlineMode ? "true" : "false";
        props["pvp"] = configuration.Pvp ? "true" : "false";
        props["white-list"] = configuration.WhiteList ? "true" : "false";
        props["gamemode"] = configuration.Gamemode;
        props["difficulty"] = configuration.Difficulty;
        props["enable-command-block"] = configuration.AllowCommandBlock ? "true" : "false";
        props["allow-flight"] = configuration.AllowFlight ? "true" : "false";
        props["allow-nether"] = configuration.AllowNether ? "true" : "false";

        foreach (var key in props.Keys.Where(key => !CorePropertyKeys.Contains(key)).ToList())
        {
            props.Remove(key);
        }

        foreach (var property in configuration.AdvancedProperties)
        {
            if (!string.IsNullOrWhiteSpace(property.Key))
            {
                props[property.Key] = property.Value;
            }
        }

        ServerPropertiesParser.Write(propsFile, props);
    }

    public bool TryGetProperty(string serverDir, string key, out string? value)
    {
        value = null;
        string propsFile = GetPropertiesPath(serverDir);
        if (!File.Exists(propsFile))
        {
            return false;
        }

        var props = ServerPropertiesParser.Read(propsFile);
        return props.TryGetValue(key, out value);
    }

    public string LoadRawProperties(string serverDir)
    {
        string propsFile = GetPropertiesPath(serverDir);
        return File.Exists(propsFile)
            ? File.ReadAllText(propsFile, Encoding.UTF8)
            : string.Empty;
    }

    public void SaveRawProperties(string serverDir, string contents)
    {
        FileUtils.AtomicWriteAllText(GetPropertiesPath(serverDir), contents, new UTF8Encoding(false));
    }

    public static bool IsCoreProperty(string key) => CorePropertyKeys.Contains(key);

    private static string GetPropertiesPath(string serverDir) =>
        Path.Combine(serverDir, ServerPropertiesFileName);
}
