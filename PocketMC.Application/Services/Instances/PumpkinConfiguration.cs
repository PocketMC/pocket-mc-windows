using System.Globalization;
using System.Net;
using System.Text;
using PocketMC.Domain.Storage;
using Tomlyn;
using Tomlyn.Model;

namespace PocketMC.Application.Services.Instances;

/// <summary>Maps PocketMC's shared settings to Pumpkin's native TOML without discarding unknown settings.</summary>
public static class PumpkinConfiguration
{
    public const string FileName = "pumpkin.toml";

    private static readonly (string Key, string Path, object Default)[] Settings =
    {
        ("motd", "networking.java.motd", "A Pumpkin Server"),
        ("max-players", "networking.java.max_players", 20L),
        ("online-mode", "networking.java.online_mode", true),
        ("view-distance", "networking.java.view_distance", 10L),
        ("simulation-distance", "networking.java.simulation_distance", 10L),
        ("gamemode", "default_gamemode", "survival"),
        ("difficulty", "default_difficulty", "normal"),
        ("level-name", "default_level_name", "world"),
        ("level-seed", "seed", ""),
        ("white-list", "white_list", false),
        ("pvp", "pvp.enabled", true),
        ("allow-nether", "allow_nether", true),
        ("spawn-protection", "spawn_protection", 16L),
        ("pumpkin-java-enabled", "networking.java.enabled", true),
        ("pumpkin-bedrock-enabled", "networking.bedrock.enabled", true),
        ("pumpkin-nethernet-enabled", "networking.bedrock.nethernet.enabled", true)
    };

    public static Dictionary<string, string> Load(string serverDir)
    {
        TomlTable table = ReadTable(serverDir);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, path, fallback) in Settings)
        {
            string value = Format(Get(table, path) ?? fallback);
            properties[key] = key is "gamemode" or "difficulty" ? value.ToLowerInvariant() : value;
        }
        string javaAddress = Format(Get(table, "networking.java.address") ?? "0.0.0.0:25565");
        string bedrockAddress = Format(Get(table, "networking.bedrock.nethernet.address") ?? "0.0.0.0:19132");
        var java = ParseAddress(javaAddress, 25565);
        var bedrock = ParseAddress(bedrockAddress, 19132);
        properties["server-port"] = java.Port.ToString(CultureInfo.InvariantCulture);
        properties["server-ip"] = java.Address;
        properties["pumpkin-bedrock-port"] = bedrock.Port.ToString(CultureInfo.InvariantCulture);
        properties["pumpkin-bedrock-ip"] = bedrock.Address;
        return properties;
    }

    public static void Save(string serverDir, IReadOnlyDictionary<string, string> properties)
    {
        TomlTable table = ReadTable(serverDir);
        Apply(table, properties);
        Write(serverDir, table);
    }

    public static Dictionary<string, string> LoadEditorProperties(string serverDir) => Flatten(ReadTable(serverDir));

    public static bool IsManagedEditorProperty(string path) =>
        Settings.Any(setting => setting.Key is not ("level-name" or "pumpkin-java-enabled" or "pumpkin-bedrock-enabled" or "pumpkin-nethernet-enabled") &&
            (path == setting.Path || path == setting.Path.Replace("networking.java.", "networking.bedrock.", StringComparison.Ordinal))) ||
        path is "networking.java.address" or "networking.bedrock.nethernet.address" or
            "commands.use_console" or "commands.use_tty" or "logging.color" or "use_favicon" or "favicon_path";

    public static void SaveEditorProperties(string serverDir, IReadOnlyDictionary<string, string> sharedProperties,
        IReadOnlyDictionary<string, string> advancedProperties)
    {
        Write(serverDir, BuildEditorTable(serverDir, sharedProperties, advancedProperties));
    }

    public static void ValidateEditorProperties(string serverDir, IReadOnlyDictionary<string, string> sharedProperties,
        IReadOnlyDictionary<string, string> advancedProperties) => ValidateTable(BuildEditorTable(serverDir, sharedProperties, advancedProperties));

    private static TomlTable BuildEditorTable(string serverDir, IReadOnlyDictionary<string, string> sharedProperties,
        IReadOnlyDictionary<string, string> advancedProperties)
    {
        TomlTable table = ReadTable(serverDir);
        Apply(table, sharedProperties);
        var rows = Flatten(table).Where(row => IsManagedEditorProperty(row.Key))
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        foreach (var row in advancedProperties)
        {
            if (string.IsNullOrWhiteSpace(row.Key)) throw new FormatException("A Pumpkin parameter needs a TOML key.");
            try
            {
                var keyTable = TomlSerializer.Deserialize<TomlTable>($"{row.Key} = true") ?? new TomlTable();
                var keys = Flatten(keyTable);
                if (keys.Count != 1) throw new FormatException("Enter one TOML key.");
                string key = keys.Single().Key;
                var valueTable = TomlSerializer.Deserialize<TomlTable>($"value = {row.Value}") ?? new TomlTable();
                if (valueTable.Count != 1 || !valueTable.ContainsKey("value"))
                    throw new FormatException("Enter one TOML value, without additional parameters.");
                if (!IsManagedEditorProperty(key)) rows.Add(key, FormatEditorValue(valueTable["value"]));
            }
            catch (Exception ex)
            {
                throw new FormatException($"Invalid Pumpkin parameter '{row.Key}'. Use valid TOML; strings need matching quotes and escaped double quotes/backslashes. {ex.Message}", ex);
            }
        }
        string contents = string.Join(Environment.NewLine, rows.Select(row => $"{row.Key} = {row.Value}"));
        table = TomlSerializer.Deserialize<TomlTable>(contents) ?? new TomlTable();
        // These settings keep redirected console commands and readiness detection usable.
        Set(table, "commands.use_console", true);
        Set(table, "commands.use_tty", false);
        Set(table, "logging.color", false);
        ValidateTable(table);
        return table;
    }

    private static Dictionary<string, string> Flatten(TomlTable table)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        void Visit(TomlTable current, string prefix)
        {
            foreach (var pair in current)
            {
                string key = prefix + FormatEditorKey(pair.Key);
                if (pair.Value is TomlTable child && child.Count > 0) Visit(child, key + ".");
                else rows.Add(key, FormatEditorValue(pair.Value));
            }
        }
        Visit(table, "");
        return rows;
    }

    private static string FormatEditorKey(string key)
    {
        string assignment = TomlSerializer.Serialize(new TomlTable { [key] = true });
        return assignment[..assignment.LastIndexOf('=')].Trim();
    }

    private static string FormatEditorValue(object value)
    {
        if (value is TomlTable table)
            return "{ " + string.Join(", ", table.Select(pair => $"{FormatEditorKey(pair.Key)} = {FormatEditorValue(pair.Value)}")) + " }";
        if (value is System.Collections.IEnumerable array && value is not string)
            return "[" + string.Join(", ", array.Cast<object>().Select(FormatEditorValue)) + "]";
        string assignment = TomlSerializer.Serialize(new TomlTable { ["value"] = value });
        return assignment[(assignment.IndexOf('=') + 1)..].Trim();
    }

    public static string CreateSafeExport(string serverDir)
    {
        var table = new TomlTable();
        Apply(table, Load(serverDir));
        TomlTable source = ReadTable(serverDir);
        // A storage-format change can make an otherwise intact world appear empty after import.
        // Retain known non-secret storage/gameplay scalars without copying plugin environments or management keys.
        foreach (string path in new[]
        {
            "world.chunk.type", "world.chunk.compression.algorithm", "world.chunk.compression.level",
            "world.lighting", "world.autosave_ticks", "world.entity_islands.unaudited_reach",
            "world.entity_islands.knockback_reach", "allow_end", "hardcore", "force_gamemode",
            "networking.bedrock.motd", "networking.bedrock.max_players", "networking.bedrock.view_distance",
            "networking.bedrock.simulation_distance", "networking.bedrock.online_mode", "networking.bedrock.authentication.enabled",
            "use_favicon", "favicon_path"
        })
        {
            object? value = Get(source, path);
            if (value == null) continue;
            if (value is not (string or bool or long or double))
                throw new FormatException($"Pumpkin setting '{path}' must be a scalar value.");
            Set(table, path, value);
        }
        return TomlSerializer.Serialize(table);
    }

    public static string ReadLevelName(string serverDir) => Load(serverDir)["level-name"];
    public static int ReadBedrockPort(string serverDir, int fallback = 19132) => ParseAddress(ReadBedrockAddress(serverDir), fallback).Port;
    public static string ReadJavaAddress(string serverDir) => Format(Get(ReadTable(serverDir), "networking.java.address") ?? "0.0.0.0:25565");
    public static string ReadBedrockAddress(string serverDir) => Format(Get(ReadTable(serverDir), "networking.bedrock.nethernet.address") ?? "0.0.0.0:19132");
    public static bool IsJavaEnabled(string serverDir) => Get(ReadTable(serverDir), "networking.java.enabled") is not false;
    public static bool IsBedrockEnabled(string serverDir)
    {
        var table = ReadTable(serverDir);
        return Get(table, "networking.bedrock.enabled") is not false && Get(table, "networking.bedrock.nethernet.enabled") is not false;
    }

    public static void PrepareForLaunch(string serverDir)
    {
        TomlTable table = ReadTable(serverDir);
        // Validate the actual instance boundary, including existing filesystem links.
        WorldInventory.Resolve(serverDir, new PocketMC.Domain.Models.InstanceMetadata { ServerType = "Pumpkin" });
        Set(table, "commands.use_console", true);
        Set(table, "commands.use_tty", false);
        Set(table, "logging.color", false);
        if (File.Exists(Path.Combine(serverDir, "server-icon.png")))
        {
            Set(table, "use_favicon", true);
            Set(table, "favicon_path", "server-icon.png");
        }
        Write(serverDir, table);
    }

    public static void ValidateRaw(string contents) => ValidateTable(TomlSerializer.Deserialize<TomlTable>(contents) ?? new TomlTable());

    private static TomlTable ReadTable(string serverDir)
    {
        string path = Path.Combine(serverDir, FileName);
        TomlTable table = File.Exists(path) ? TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path)) ?? new TomlTable() : new TomlTable();
        ValidateTable(table);
        return table;
    }

    private static void Apply(TomlTable table, IReadOnlyDictionary<string, string> properties)
    {
        foreach (var (key, path, fallback) in Settings)
        {
            if (!properties.TryGetValue(key, out string? value)) continue;
            object converted = fallback switch
            {
                bool => bool.TryParse(value, out bool parsed) ? parsed : throw new FormatException($"{key} must be true or false."),
                long => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) && parsed >= (key == "spawn-protection" ? 0 : 1)
                    ? parsed : throw new FormatException($"{key} must be a positive integer."),
                _ => value
            };
            if (key is "view-distance" or "simulation-distance" && converted is long distance && (distance < 2 || distance > 64))
                throw new FormatException($"{key} must be between 2 and 64.");
            if (key is "gamemode" or "difficulty")
            {
                string[] allowed = key == "gamemode" ? new[] { "survival", "creative", "adventure", "spectator" } : new[] { "peaceful", "easy", "normal", "hard" };
                if (!allowed.Contains(value.ToLowerInvariant())) throw new FormatException($"Invalid {key}: {value}.");
                converted = char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
            }
            Set(table, path, converted);
            if (key is "motd" or "max-players" or "view-distance" or "simulation-distance" or "online-mode")
                Set(table, path.Replace("networking.java.", "networking.bedrock.", StringComparison.Ordinal), converted);
        }
        if (properties.TryGetValue("server-port", out string? port))
            Set(table, "networking.java.address", BuildAddress(properties.GetValueOrDefault("server-ip") ?? "0.0.0.0", port, false));
        if (properties.TryGetValue("pumpkin-bedrock-port", out string? bedrockPort))
            Set(table, "networking.bedrock.nethernet.address", BuildAddress(properties.GetValueOrDefault("pumpkin-bedrock-ip") ?? "0.0.0.0", bedrockPort, true));
        Set(table, "commands.use_console", true);
        Set(table, "commands.use_tty", false);
        Set(table, "logging.color", false);
        if (Get(table, "logging.file") == null) Set(table, "logging.file", "latest.log");
    }

    private static string BuildAddress(string address, string portText, bool bedrock)
    {
        int upper = bedrock ? 65534 : 65535;
        if (!int.TryParse(portText, out int port) || port < 1 || port > upper)
            throw new FormatException($"{(bedrock ? "Pumpkin Bedrock" : "Java")} port must be between 1 and {upper}.");
        if (string.IsNullOrWhiteSpace(address)) address = "0.0.0.0";
        if (!IPAddress.TryParse(address.Trim('[', ']'), out IPAddress? ip))
            throw new FormatException("Pumpkin bind address must be an IPv4 or IPv6 address.");
        return new IPEndPoint(ip, port).ToString();
    }

    private static (string Address, int Port) ParseAddress(string value, int fallback)
    {
        if (!IPEndPoint.TryParse(value, out IPEndPoint? endpoint))
            throw new FormatException($"Invalid Pumpkin listener address '{value}'. Expected an IP address and port.");
        if (endpoint.Port == 0) throw new FormatException("Pumpkin listener ports must be between 1 and 65535.");
        return (endpoint.Address.ToString(), endpoint.Port);
    }

    private static object? Get(TomlTable root, string path)
    {
        object? value = root;
        foreach (string part in path.Split('.'))
        {
            if (value is not TomlTable table) throw new FormatException($"Pumpkin setting '{path}' requires nested TOML tables.");
            if (!table.TryGetValue(part, out value)) return null;
        }
        return value;
    }

    private static void ValidateTable(TomlTable table)
    {
        foreach (var (key, path, fallback) in Settings)
        {
            object? value = Get(table, path);
            if (value == null) continue;
            if (fallback is bool && value is not bool || fallback is long && value is not long || fallback is string && value is not string)
                throw new FormatException($"Pumpkin setting '{path}' has an invalid TOML value type.");
            if (key is "view-distance" or "simulation-distance" && value is long distance && (distance < 2 || distance > 64))
                throw new FormatException($"Pumpkin setting '{path}' must be between 2 and 64.");
            if (key == "max-players" && value is long maxPlayers && (maxPlayers < 1 || maxPlayers > uint.MaxValue))
                throw new FormatException("Pumpkin maximum players must be a positive 32-bit integer.");
            if (key == "spawn-protection" && value is long protection && (protection < 0 || protection > uint.MaxValue))
                throw new FormatException("Pumpkin spawn protection must be a nonnegative 32-bit integer.");
            if (key == "gamemode" && value is string mode && !new[] { "Survival", "Creative", "Adventure", "Spectator" }.Contains(mode))
                throw new FormatException("Pumpkin default_gamemode must use Survival, Creative, Adventure, or Spectator.");
            if (key == "difficulty" && value is string difficulty && !new[] { "Peaceful", "Easy", "Normal", "Hard" }.Contains(difficulty))
                throw new FormatException("Pumpkin default_difficulty must use Peaceful, Easy, Normal, or Hard.");
        }
        if (Get(table, "default_level_name") is string levelName)
        {
            string[] segments = levelName.Split('/', '\\');
            if (string.IsNullOrWhiteSpace(levelName) || Path.IsPathRooted(levelName) ||
                segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                segments[0].StartsWith('.') || new[] { "plugins", "mods", "libraries", "config", "backups", "runtime", "data", "logs" }.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
                throw new FormatException("Pumpkin's world must be a dedicated directory inside the instance.");
        }
        bool javaEnabled = Get(table, "networking.java.enabled") is not false;
        bool bedrockEnabled = Get(table, "networking.bedrock.enabled") is not false && Get(table, "networking.bedrock.nethernet.enabled") is not false;
        if (!javaEnabled && !bedrockEnabled)
            throw new FormatException("Enable at least one Pumpkin client listener (Java or Bedrock).");
        object? javaValue = Get(table, "networking.java.address");
        if (javaValue != null && javaValue is not string) throw new FormatException("Pumpkin Java address must be a TOML string.");
        if (javaValue is string java) ParseAddress(java, 25565);
        object? bedrockValue = Get(table, "networking.bedrock.nethernet.address");
        if (bedrockValue != null && bedrockValue is not string) throw new FormatException("Pumpkin Bedrock address must be a TOML string.");
        if (bedrockValue is string bedrock)
        {
            if (ParseAddress(bedrock, 19132).Port >= 65535) throw new FormatException("Pumpkin Bedrock port must leave room for the following IPv6 UDP port.");
        }
        if (javaEnabled && bedrockEnabled &&
            ParseAddress(javaValue as string ?? "0.0.0.0:25565", 25565).Port ==
            ParseAddress(bedrockValue as string ?? "0.0.0.0:19132", 19132).Port)
            throw new FormatException("Pumpkin Java and Bedrock listeners must use different ports.");
    }

    private static void Set(TomlTable root, string path, object value)
    {
        string[] parts = path.Split('.');
        TomlTable table = root;
        foreach (string part in parts[..^1])
        {
            if (!table.TryGetValue(part, out object? child)) table[part] = child = new TomlTable();
            table = child as TomlTable ?? throw new FormatException($"Pumpkin setting '{part}' must be a TOML table.");
        }
        table[parts[^1]] = value;
    }

    private static string Format(object value) => value is bool flag ? (flag ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static void Write(string serverDir, TomlTable table)
    {
        ValidateTable(table);
        FileUtils.AtomicWriteAllText(Path.Combine(serverDir, FileName), TomlSerializer.Serialize(table), new UTF8Encoding(false));
    }
}
