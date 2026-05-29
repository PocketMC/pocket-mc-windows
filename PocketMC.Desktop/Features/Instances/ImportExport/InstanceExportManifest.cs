using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocketMC.Desktop.Features.Instances.ImportExport;

public sealed class InstanceExportManifest
{
    public const string CurrentExportVersion = "1.0";

    [JsonPropertyName("exportVersion")]
    public string ExportVersion { get; set; } = CurrentExportVersion;

    [JsonPropertyName("origin")]
    public InstanceExportOrigin Origin { get; set; } = new();

    [JsonPropertyName("serverMeta")]
    public InstanceExportServerMeta ServerMeta { get; set; } = new();

    [JsonPropertyName("software")]
    [JsonConverter(typeof(ServerSoftwareManifestJsonConverter))]
    public ServerSoftwareManifest Software { get; set; } = new JavaServerSoftwareManifest();

    [JsonPropertyName("runtime")]
    public InstanceRuntimeManifest Runtime { get; set; } = new();

    [JsonPropertyName("addons")]
    public List<InstanceAddonManifest> Addons { get; set; } = new();

    public static JsonSerializerOptions CreateJsonOptions(bool writeIndented = true) => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = writeIndented,
        Converters =
        {
            new JsonStringEnumConverter(),
            new InstanceAddonManifestJsonConverter()
        }
    };
}

public sealed class InstanceExportOrigin
{
    [JsonPropertyName("pocketMcVersion")]
    public string PocketMcVersion { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InstanceExportServerMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

public enum InstanceServerPlatform
{
    Java,
    Bedrock
}

public abstract class ServerSoftwareManifest
{
    [JsonIgnore]
    public abstract InstanceServerPlatform Platform { get; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("minecraftVersion")]
    public string MinecraftVersion { get; set; } = string.Empty;

    [JsonPropertyName("loaderVersion")]
    public string? LoaderVersion { get; set; }
}

public sealed class JavaServerSoftwareManifest : ServerSoftwareManifest
{
    public override InstanceServerPlatform Platform => InstanceServerPlatform.Java;
}

public sealed class BedrockServerSoftwareManifest : ServerSoftwareManifest
{
    public override InstanceServerPlatform Platform => InstanceServerPlatform.Bedrock;
}

public enum InstanceRuntimeType
{
    Native,
    Java
}

public sealed class InstanceRuntimeManifest
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InstanceRuntimeType Type { get; set; } = InstanceRuntimeType.Java;

    [JsonPropertyName("targetVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetVersion { get; set; }
}

public enum InstanceAddonPlatform
{
    Java,
    Bedrock
}

public static class InstanceAddonTypes
{
    public const string Mod = "mod";
    public const string Plugin = "plugin";
    public const string Datapack = "datapack";
    public const string BehaviorPack = "behavior_pack";
    public const string ResourcePack = "resource_pack";
    public const string McAddon = "mcaddon";
}

[JsonConverter(typeof(InstanceAddonManifestJsonConverter))]
public abstract class InstanceAddonManifest
{
    [JsonIgnore]
    public abstract InstanceAddonPlatform Platform { get; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "Local";

    [JsonPropertyName("fileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    [JsonPropertyName("relativePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelativePath { get; set; }
}

public sealed class JavaAddonManifest : InstanceAddonManifest
{
    public override InstanceAddonPlatform Platform => InstanceAddonPlatform.Java;

    [JsonPropertyName("projectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; set; }

    [JsonPropertyName("versionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionId { get; set; }

    [JsonPropertyName("hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; set; }

    [JsonPropertyName("loader")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Loader { get; set; }
}

public sealed class BedrockAddonManifest : InstanceAddonManifest
{
    public override InstanceAddonPlatform Platform => InstanceAddonPlatform.Bedrock;

    [JsonPropertyName("uuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uuid { get; set; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    [JsonPropertyName("hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; set; }

    [JsonPropertyName("downloadUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadUrl { get; set; }
}

public sealed class ServerSoftwareManifestJsonConverter : JsonConverter<ServerSoftwareManifest>
{
    public override ServerSoftwareManifest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        string platform = ManifestJson.ReadRequiredString(root, "platform");
        ServerSoftwareManifest manifest = ManifestJson.PlatformEquals(platform, InstanceServerPlatform.Java)
            ? new JavaServerSoftwareManifest()
            : ManifestJson.PlatformEquals(platform, InstanceServerPlatform.Bedrock)
                ? new BedrockServerSoftwareManifest()
                : throw new JsonException($"Unsupported server software platform '{platform}'.");

        manifest.Type = ManifestJson.ReadString(root, "type") ?? string.Empty;
        manifest.MinecraftVersion = ManifestJson.ReadString(root, "minecraftVersion") ?? string.Empty;
        manifest.LoaderVersion = ManifestJson.ReadString(root, "loaderVersion");
        return manifest;
    }

    public override void Write(Utf8JsonWriter writer, ServerSoftwareManifest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("platform", value.Platform.ToString());
        writer.WriteString("type", value.Type);
        writer.WriteString("minecraftVersion", value.MinecraftVersion);

        if (value.LoaderVersion is null)
        {
            writer.WriteNull("loaderVersion");
        }
        else
        {
            writer.WriteString("loaderVersion", value.LoaderVersion);
        }

        writer.WriteEndObject();
    }
}

public sealed class InstanceAddonManifestJsonConverter : JsonConverter<InstanceAddonManifest>
{
    public override InstanceAddonManifest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        string type = ManifestJson.ReadString(root, "type") ?? string.Empty;

        InstanceAddonManifest manifest = IsBedrockAddonType(type)
            ? new BedrockAddonManifest
            {
                Uuid = ManifestJson.ReadString(root, "uuid"),
                Version = ManifestJson.ReadString(root, "version"),
                Hash = ManifestJson.ReadString(root, "hash"),
                DownloadUrl = ManifestJson.ReadString(root, "downloadUrl")
            }
            : new JavaAddonManifest
            {
                ProjectId = ManifestJson.ReadString(root, "projectId"),
                VersionId = ManifestJson.ReadString(root, "versionId"),
                Hash = ManifestJson.ReadString(root, "hash"),
                Loader = ManifestJson.ReadString(root, "loader")
            };

        manifest.Name = ManifestJson.ReadString(root, "name") ?? string.Empty;
        manifest.Type = type;
        manifest.Provider = ManifestJson.ReadString(root, "provider") ?? "Local";
        manifest.FileName = ManifestJson.ReadString(root, "fileName");
        manifest.RelativePath = ManifestJson.ReadString(root, "relativePath");
        return manifest;
    }

    public override void Write(Utf8JsonWriter writer, InstanceAddonManifest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteString("type", value.Type);
        writer.WriteString("provider", value.Provider);
        WriteOptionalString(writer, "fileName", value.FileName);
        WriteOptionalString(writer, "relativePath", value.RelativePath);

        switch (value)
        {
            case JavaAddonManifest java:
                WriteOptionalString(writer, "projectId", java.ProjectId);
                WriteOptionalString(writer, "versionId", java.VersionId);
                WriteOptionalString(writer, "hash", java.Hash);
                WriteOptionalString(writer, "loader", java.Loader);
                break;
            case BedrockAddonManifest bedrock:
                WriteOptionalString(writer, "uuid", bedrock.Uuid);
                WriteOptionalString(writer, "version", bedrock.Version);
                WriteOptionalString(writer, "hash", bedrock.Hash);
                WriteOptionalString(writer, "downloadUrl", bedrock.DownloadUrl);
                break;
        }

        writer.WriteEndObject();
    }

    private static bool IsBedrockAddonType(string type) =>
        type.Equals(InstanceAddonTypes.BehaviorPack, StringComparison.OrdinalIgnoreCase) ||
        type.Equals(InstanceAddonTypes.ResourcePack, StringComparison.OrdinalIgnoreCase) ||
        type.Equals(InstanceAddonTypes.McAddon, StringComparison.OrdinalIgnoreCase);

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(propertyName, value);
        }
    }
}

internal static class ManifestJson
{
    public static string ReadRequiredString(JsonElement element, string propertyName) =>
        ReadString(element, propertyName) ?? throw new JsonException($"Missing required manifest property '{propertyName}'.");

    public static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    public static bool PlatformEquals(string value, InstanceServerPlatform platform) =>
        value.Equals(platform.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (JsonProperty candidate in element.EnumerateObject())
        {
            if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
