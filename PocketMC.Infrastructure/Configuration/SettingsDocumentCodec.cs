using System.Text.Json;
using System.Text.Json.Nodes;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Configuration;

internal static class SettingsDocumentCodec
{
    public const int CurrentFormatVersion = 3;
    public const string FormatVersionProperty = "formatVersion";
    public const string SnapshotSuffix = ".sectioned-v3.json";

    private static readonly JsonSerializerOptions FlatOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SectionOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, string[]> SectionProperties =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["application"] =
            [
                nameof(AppSettings.SchemaVersion), nameof(AppSettings.AppRootPath),
                nameof(AppSettings.HasCompletedFirstLaunch), nameof(AppSettings.StartWithWindows),
                nameof(AppSettings.StartMinimizedToTray), nameof(AppSettings.MinimizeToTrayOnClose),
                nameof(AppSettings.KeepComputerAwakeWhileServersRunning), nameof(AppSettings.PlayitConfigDirectory),
                nameof(AppSettings.PlayitVersion), nameof(AppSettings.ExternalBackupDirectory),
                nameof(AppSettings.ConsoleBufferSize), nameof(AppSettings.LastSeenChangelogVersion)
            ],
            ["appearance"] =
            [
                nameof(AppSettings.WindowBackdrop), nameof(AppSettings.AccentColorMode),
                nameof(AppSettings.CustomAccentColor), nameof(AppSettings.HasMigratedToGreenWallpaperBlurTheme),
                nameof(AppSettings.HasMigratedToDefaultImageWallpaper), nameof(AppSettings.CustomBackgroundImagePath),
                nameof(AppSettings.WallpaperBlurRadius), nameof(AppSettings.WallpaperTintOpacity)
            ],
            ["window"] =
            [
                nameof(AppSettings.WindowWidth), nameof(AppSettings.WindowHeight), nameof(AppSettings.IsWindowMaximized)
            ],
            ["ai"] =
            [
                nameof(AppSettings.AiApiKey), nameof(AppSettings.AiApiKeys), nameof(AppSettings.AiModels),
                nameof(AppSettings.AiEndpoints), nameof(AppSettings.EnableAiSummarization),
                nameof(AppSettings.AiProvider), nameof(AppSettings.AlwaysAutoSummarize), nameof(AppSettings.OllamaMode)
            ],
            ["marketplace"] = [nameof(AppSettings.CurseForgeApiKey)],
            ["telemetry"] =
            [
                nameof(AppSettings.EnableTelemetry), nameof(AppSettings.TelemetryClientId),
                nameof(AppSettings.HasReportedInstall)
            ],
            ["java"] = [nameof(AppSettings.UserRemovedJavaVersions)],
            ["backups"] = [nameof(AppSettings.CloudBackups)],
            ["cloud"] = [nameof(AppSettings.CloudTokens)],
            ["remoteControl"] = [nameof(AppSettings.RemoteControl)],
            ["playit"] = [nameof(AppSettings.PlayitPartnerConnection)],
            ["discord"] =
            [
                nameof(AppSettings.DiscordRpc), nameof(AppSettings.EnableDiscordRpc), nameof(AppSettings.DiscordUserId),
                nameof(AppSettings.DiscordApiUrl), nameof(AppSettings.DiscordApiKey),
                nameof(AppSettings.EnableDiscordNotifications), nameof(AppSettings.EnableServerOnlineNotifications),
                nameof(AppSettings.EnableAgentConnectNotifications), nameof(AppSettings.EnableRemoteControlNotifications),
                nameof(AppSettings.EnableAiSummaryNotifications)
            ]
        };

    private static readonly IReadOnlyDictionary<string, string> PropertySections =
        SectionProperties
            .SelectMany(section => section.Value.Select(property => new KeyValuePair<string, string>(property, section.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public static string GetSnapshotPath(string settingsPath) => settingsPath + SnapshotSuffix;

    public static int GetFormatVersion(JsonObject document)
    {
        if (!document.TryGetPropertyValue(FormatVersionProperty, out JsonNode? versionNode))
        {
            return 0;
        }

        if (versionNode is JsonValue value && value.TryGetValue(out int version))
        {
            return version;
        }

        throw new JsonException("Settings formatVersion must be an integer.");
    }

    public static bool HasSectionForProperty(string propertyName) => PropertySections.ContainsKey(propertyName);

    public static JsonObject Serialize(AppSettings settings, JsonObject? previousDocument = null)
    {
        JsonObject flat = JsonSerializer.SerializeToNode(settings, FlatOptions)?.AsObject()
            ?? throw new JsonException("Could not serialize settings.");
        JsonObject camelCase = JsonSerializer.SerializeToNode(settings, SectionOptions)?.AsObject()
            ?? throw new JsonException("Could not serialize sectioned settings.");

        JsonObject document = previousDocument == null
            ? new JsonObject()
            : (JsonObject)previousDocument.DeepClone();
        document[FormatVersionProperty] = CurrentFormatVersion;

        foreach ((string sectionName, string[] propertyNames) in SectionProperties)
        {
            JsonObject section = document[sectionName] as JsonObject ?? new JsonObject();
            foreach (string propertyName in propertyNames)
            {
                string jsonName = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
                if (camelCase.TryGetPropertyValue(jsonName, out JsonNode? value))
                {
                    section[jsonName] = value?.DeepClone();
                }
                else
                {
                    section.Remove(jsonName);
                    document.Remove(propertyName);
                }
            }
            document[sectionName] = section;
        }

        foreach ((string propertyName, JsonNode? value) in flat)
        {
            document[propertyName] = value?.DeepClone();
        }

        return document;
    }

    public static JsonObject ToLegacyFlat(JsonObject document)
    {
        JsonObject flat = new();
        foreach ((string sectionName, string[] propertyNames) in SectionProperties)
        {
            if (document[sectionName] is not JsonObject section)
            {
                continue;
            }

            foreach (string propertyName in propertyNames)
            {
                string jsonName = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
                if (section.TryGetPropertyValue(jsonName, out JsonNode? value))
                {
                    flat[propertyName] = value?.DeepClone();
                }
            }
        }

        foreach ((string propertyName, JsonNode? value) in document)
        {
            if (propertyName == FormatVersionProperty ||
                PropertySections.ContainsKey(propertyName) ||
                SectionProperties.ContainsKey(propertyName))
            {
                continue;
            }

            if (!flat.ContainsKey(propertyName))
            {
                flat[propertyName] = value?.DeepClone();
            }
        }

        flat[nameof(AppSettings.SchemaVersion)] = 2;
        return flat;
    }

    public static JsonObject MergeLegacyValues(JsonObject snapshot, JsonObject legacyDocument)
    {
        JsonObject merged = (JsonObject)snapshot.DeepClone();
        foreach ((string propertyName, JsonNode? value) in legacyDocument)
        {
            if (PropertySections.ContainsKey(propertyName) || propertyName == nameof(AppSettings.SchemaVersion))
            {
                merged[propertyName] = value?.DeepClone();
                if (PropertySections.TryGetValue(propertyName, out string? sectionName) &&
                    merged[sectionName] is JsonObject section)
                {
                    string jsonName = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
                    section[jsonName] = value?.DeepClone();
                }
            }
        }
        return merged;
    }

    public static JsonObject ParseObject(string json)
        => JsonNode.Parse(json)?.AsObject()
            ?? throw new JsonException("Settings document must be a JSON object.");

    public static AppSettings DeserializeFlat(JsonObject flat)
        => JsonSerializer.Deserialize<AppSettings>(flat.ToJsonString(), FlatOptions)
            ?? throw new JsonException("Settings document did not contain settings.");
}
