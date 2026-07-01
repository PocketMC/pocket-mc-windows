using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Application.Instances.Services;

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
        "allow-list",
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

        // ── Shared render/sim distance ────────────────────────────────────
        "view-distance",            // render distance (chunks)
        "simulation-distance",      // simulation distance (chunks, Java only)

        // ── PocketMine-MP ─────────────────────────────────────────────────
        "server-name",              // PM uses this instead of motd
        "enable-query",
        "auto-save",
        "language",
    };

    private readonly InstanceManager _instanceManager;
    private readonly PocketMC.Desktop.Helpers.IGeyserDetector _geyserDetector;

    public ServerConfigurationService(InstanceManager instanceManager, PocketMC.Desktop.Helpers.IGeyserDetector geyserDetector)
    {
        _instanceManager = instanceManager;
        _geyserDetector = geyserDetector;
    }

    public ServerConfiguration Load(InstanceMetadata metadata, string serverDir)
    {
        var props = ServerPropertiesParser.Read(GetPropertiesPath(serverDir));
        var profile = ServerSettingsProfile.FromMetadata(metadata, serverDir, _geyserDetector);

        // Sync metadata if needed (NET-15)
        if (TryGetDisplayName(props, profile, out var pMotd)) metadata.Motd = pMotd;
        if (props.TryGetValue("max-players", out var pMax) && int.TryParse(pMax, out int max)) metadata.MaxPlayers = max;
        if (props.TryGetValue("server-port", out var pPort) && int.TryParse(pPort, out int parsedPort)) metadata.ServerPort = parsedPort;

        string defaultPort = profile.DefaultServerPort;
        string defaultPortV6 = TryGetPortPlusOne(props.TryGetValue("server-port", out var portValue) ? portValue : defaultPort);

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
            Motd = TryGetDisplayName(props, profile, out var motd) ? motd : "A Minecraft Server",
            Seed = props.TryGetValue("level-seed", out var seed) ? seed : "",
            SpawnProtection = props.TryGetValue("spawn-protection", out var protection) ? protection : "16",
            MaxPlayers = props.TryGetValue("max-players", out var maxPlayers) ? maxPlayers : "20",
            ServerPort = props.TryGetValue("server-port", out var port) ? port : defaultPort,
            ServerPortV6 = props.TryGetValue("server-portv6", out var portV6) ? portV6 : defaultPortV6,
            ServerIp = props.TryGetValue("server-ip", out var ip) ? ip : "",
            LevelType = props.TryGetValue("level-type", out var levelType) ? levelType : profile.DefaultLevelType,
            OnlineMode = TryGetBool(props, "online-mode"),
            Pvp = props.TryGetValue("pvp", out var pvp) ? pvp == "true" : true,
            WhiteList = TryGetBool(props, "white-list") || TryGetBool(props, "allow-list"),
            Gamemode = props.TryGetValue("gamemode", out var gamemode) ? gamemode : "survival",
            Difficulty = props.TryGetValue("difficulty", out var difficulty) ? difficulty : "easy",
            AllowCommandBlock = TryGetBool(props, "enable-command-block"),
            AllowFlight = TryGetBool(props, "allow-flight"),
            AllowNether = props.TryGetValue("allow-nether", out var allowNether) ? allowNether == "true" : true,
            AllowCheats = TryGetBool(props, "allow-cheats"),
            TexturepackRequired = TryGetBool(props, "texturepack-required"),
            ForceGamemode = TryGetBool(props, "force-gamemode"),
            DefaultPlayerPermissionLevel = props.TryGetValue("default-player-permission-level", out var permission) ? permission : "member",
            TickDistance = props.TryGetValue("tick-distance", out var tickDistance) ? tickDistance : "4",
            ViewDistance = props.TryGetValue("view-distance", out var viewDist) ? viewDist : (profile.IsJava ? "10" : "32"),
            SimulationDistance = props.TryGetValue("simulation-distance", out var simDist) ? simDist : "10"
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
        var profile = ServerSettingsProfile.FromMetadata(metadata, serverDir, _geyserDetector);

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
        if (int.TryParse(configuration.ServerPort, out int sp)) metadata.ServerPort = sp;

        _instanceManager.SaveMetadata(metadata, serverDir);

        var propsFile = GetPropertiesPath(serverDir);
        var props = ServerPropertiesParser.Read(propsFile);

        props[profile.DisplayNamePropertyKey] = configuration.Motd;
        if (profile.IsJava)
        {
            props.Remove("server-name");
        }
        else
        {
            props.Remove("motd");
        }

        if (!string.IsNullOrWhiteSpace(configuration.Seed))
        {
            props["level-seed"] = configuration.Seed;
        }

        props["max-players"] = configuration.MaxPlayers;
        props["server-port"] = configuration.ServerPort;

        if (!string.IsNullOrWhiteSpace(configuration.ServerIp))
        {
            props["server-ip"] = configuration.ServerIp;
        }
        else
        {
            props.Remove("server-ip");
        }

        props["online-mode"] = configuration.OnlineMode ? "true" : "false";
        props["pvp"] = configuration.Pvp ? "true" : "false";
        // Bedrock/PocketMine use "allow-list", Java uses "white-list"
        if (profile.IsJava)
        {
            props["white-list"] = configuration.WhiteList ? "true" : "false";
            props.Remove("allow-list");
        }
        else
        {
            props["allow-list"] = configuration.WhiteList ? "true" : "false";
            props.Remove("white-list");
        }
        props["gamemode"] = configuration.Gamemode;
        props["difficulty"] = configuration.Difficulty;

        // Render / Simulation distance (shared across all engines)
        if (!string.IsNullOrWhiteSpace(configuration.ViewDistance))
            props["view-distance"] = configuration.ViewDistance;

        if (profile.IsJava)
        {
            props["spawn-protection"] = configuration.SpawnProtection;
            props["level-type"] = configuration.LevelType;
            props["enable-command-block"] = configuration.AllowCommandBlock ? "true" : "false";
            props["allow-flight"] = configuration.AllowFlight ? "true" : "false";
            props["allow-nether"] = configuration.AllowNether ? "true" : "false";
            if (!string.IsNullOrWhiteSpace(configuration.SimulationDistance))
                props["simulation-distance"] = configuration.SimulationDistance;
            RemoveBedrockProperties(props);
        }
        else
        {
            RemoveJavaOnlyProperties(props);

            props["server-portv6"] = string.IsNullOrWhiteSpace(configuration.ServerPortV6)
                ? TryGetPortPlusOne(configuration.ServerPort)
                : configuration.ServerPortV6;
            props["allow-cheats"] = configuration.AllowCheats ? "true" : "false";
            props["texturepack-required"] = configuration.TexturepackRequired ? "true" : "false";
            props["force-gamemode"] = configuration.ForceGamemode ? "true" : "false";
            props["default-player-permission-level"] = string.IsNullOrWhiteSpace(configuration.DefaultPlayerPermissionLevel)
                ? "member"
                : configuration.DefaultPlayerPermissionLevel;
            props["tick-distance"] = string.IsNullOrWhiteSpace(configuration.TickDistance)
                ? "4"
                : configuration.TickDistance;
            props.Remove("simulation-distance"); // Not a Bedrock/PM property
        }

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

    public void SaveProperty(string serverDir, string key, string value)
    {
        string propsFile = GetPropertiesPath(serverDir);
        var props = ServerPropertiesParser.Read(propsFile);
        props[key] = value;
        ServerPropertiesParser.Write(propsFile, props);
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

    private static bool TryGetDisplayName(
        IReadOnlyDictionary<string, string> props,
        ServerSettingsProfile profile,
        out string value)
    {
        if (props.TryGetValue(profile.DisplayNamePropertyKey, out value!))
        {
            return true;
        }

        string fallbackKey = profile.IsJava ? "server-name" : "motd";
        return props.TryGetValue(fallbackKey, out value!);
    }

    private static bool TryGetBool(IReadOnlyDictionary<string, string> props, string key)
    {
        return props.TryGetValue(key, out var value) &&
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetPortPlusOne(string rawPort)
    {
        return int.TryParse(rawPort, out int parsedPort) && parsedPort < 65535
            ? (parsedPort + 1).ToString()
            : "19133";
    }

    private static void RemoveJavaOnlyProperties(IDictionary<string, string> props)
    {
        props.Remove("spawn-protection");
        props.Remove("level-type");
        props.Remove("enable-command-block");
        props.Remove("allow-nether");
    }

    private static void RemoveBedrockProperties(IDictionary<string, string> props)
    {
        props.Remove("server-portv6");
        props.Remove("allow-cheats");
        props.Remove("texturepack-required");
        props.Remove("force-gamemode");
        props.Remove("default-player-permission-level");
        props.Remove("tick-distance");
        props.Remove("emit-server-telemetry");
    }
}

