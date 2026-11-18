using System.Globalization;
using System.Text.Json;

namespace PocketMC.Desktop.Localization;

/// <summary>
/// One language pack. Keys are the English source strings, matching gettext-style lookup.
/// </summary>
public sealed class LanguageCatalog
{
    public const string DefaultDateFormat = "MMM d, yyyy";
    public const string DefaultDateTimeFormat = "MMM d, yyyy h:mm tt";

    private LanguageCatalog(
        string language,
        string displayName,
        string cultureName,
        string dateFormat,
        string dateTimeFormat,
        IReadOnlyDictionary<string, string> strings)
    {
        Language = language;
        DisplayName = displayName;
        Culture = ResolveCulture(cultureName);
        DateFormat = dateFormat;
        DateTimeFormat = dateTimeFormat;
        Strings = strings;
    }

    public string Language { get; }
    public string DisplayName { get; }
    public CultureInfo Culture { get; }
    public string DateFormat { get; }
    public string DateTimeFormat { get; }
    public IReadOnlyDictionary<string, string> Strings { get; }

    public static LanguageCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FormatException("Language catalog is empty.");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Language catalog must be a JSON object.");
        }

        string language = ReadRequiredString(root, "language");
        string displayName = ReadOptionalString(root, "displayName") ?? language;
        string cultureName = ReadOptionalString(root, "culture") ?? language;
        string dateFormat = ReadOptionalString(root, "dateFormat") ?? DefaultDateFormat;
        string dateTimeFormat = ReadOptionalString(root, "dateTimeFormat") ?? DefaultDateTimeFormat;

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("strings", out JsonElement stringsElement) && stringsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in stringsElement.EnumerateObject())
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                strings[entry.Name] = entry.Value.GetString() ?? string.Empty;
            }
        }

        return new LanguageCatalog(language, displayName, cultureName, dateFormat, dateTimeFormat, strings);
    }

    public static LanguageCatalog? TryParse(string json, out string? error)
    {
        try
        {
            error = null;
            return Parse(json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        string? value = ReadOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Language catalog is missing '{propertyName}'.");
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static CultureInfo ResolveCulture(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
