using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PocketMC.Infrastructure.News;

public enum NewsType
{
    Announcement,
    QuickFix,
    Critical,
    Maintenance,
    Update,
    Security
}

public enum NewsPriority
{
    Normal,
    Important,
    Critical
}

public enum NewsBlockType
{
    Title,
    Subtitle,
    Heading,
    Paragraph,
    BulletList,
    NumberedList,
    Warning,
    Important,
    Code,
    Link,
    Divider
}

public sealed record NewsMetadata(
    string Id,
    NewsType Type,
    NewsPriority Priority,
    bool Popup,
    DateTimeOffset PublishedUtc,
    DateTimeOffset? ExpiresUtc,
    Version? MinimumVersion,
    Version? MaximumVersion);

public sealed record NewsContentBlock(
    NewsBlockType Type,
    string Text = "",
    IReadOnlyList<string>? Items = null,
    Uri? Link = null);

public sealed record NewsItem(
    string FileName,
    string RawText,
    NewsMetadata Metadata,
    IReadOnlyList<NewsContentBlock> Blocks);

public sealed class NewsTextParser
{
    private static readonly HashSet<string> MetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "type", "priority", "popup", "published", "expires", "minVersion", "maxVersion"
    };

    public NewsItem Parse(string fileName, string source)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
            !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("News filename must be a simple .txt filename.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidDataException("News file is empty.");
        }

        string normalized = source.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        if (lines.Length < 4 || lines[0].Trim() != "---")
        {
            throw new InvalidDataException("News file must begin with a metadata section delimited by --- lines.");
        }

        int metadataEnd = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (metadataEnd < 0)
        {
            throw new InvalidDataException("News metadata section is not closed.");
        }

        Dictionary<string, string> values = ParseMetadata(lines, metadataEnd);
        NewsMetadata metadata = BuildMetadata(values);
        IReadOnlyList<NewsContentBlock> blocks = ParseContent(lines, metadataEnd + 1);
        if (!blocks.Any(block => block.Type == NewsBlockType.Title))
        {
            throw new InvalidDataException("News content must contain exactly one title.");
        }

        return new NewsItem(fileName, normalized, metadata, blocks);
    }

    public NewsMetadata ParseMetadataHeader(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidDataException("News metadata is empty.");
        }

        string normalized = source.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
        {
            throw new InvalidDataException("News file must begin with a metadata section delimited by --- lines.");
        }

        int metadataEnd = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (metadataEnd < 0)
        {
            throw new InvalidDataException("News metadata section is not closed.");
        }

        return BuildMetadata(ParseMetadata(lines, metadataEnd));
    }

    private static Dictionary<string, string> ParseMetadata(string[] lines, int metadataEnd)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < metadataEnd; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidDataException($"Invalid metadata line: {line}");
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (!MetadataKeys.Contains(key))
            {
                throw new InvalidDataException($"Unsupported metadata field '{key}'.");
            }

            if (!values.TryAdd(key, value))
            {
                throw new InvalidDataException($"Metadata field '{key}' is duplicated.");
            }
        }

        return values;
    }

    private static NewsMetadata BuildMetadata(IReadOnlyDictionary<string, string> values)
    {
        string id = Required(values, "id");
        if (id.Length > 128 || !Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new InvalidDataException("News id contains unsupported characters or is too long.");
        }

        if (!Enum.TryParse(ToEnumName(Required(values, "type")), ignoreCase: true, out NewsType type) ||
            !Enum.IsDefined(type))
        {
            throw new InvalidDataException("News type is not supported.");
        }

        if (!Enum.TryParse(Required(values, "priority"), ignoreCase: true, out NewsPriority priority) ||
            !Enum.IsDefined(priority))
        {
            throw new InvalidDataException("News priority is not supported.");
        }

        if (!bool.TryParse(Required(values, "popup"), out bool popup))
        {
            throw new InvalidDataException("News popup must be true or false.");
        }

        DateTimeOffset published = ParseUtcTimestamp(Required(values, "published"), "published");
        DateTimeOffset? expires = Optional(values, "expires") is { Length: > 0 } expiryValue
            ? ParseUtcTimestamp(expiryValue, "expires")
            : null;
        Version? minimumVersion = ParseOptionalVersion(Optional(values, "minVersion"), "minVersion");
        Version? maximumVersion = ParseOptionalVersion(Optional(values, "maxVersion"), "maxVersion");
        if (minimumVersion != null && maximumVersion != null && minimumVersion > maximumVersion)
        {
            throw new InvalidDataException("minVersion must not be greater than maxVersion.");
        }

        if (expires != null && expires <= published)
        {
            throw new InvalidDataException("expires must be later than published.");
        }

        return new NewsMetadata(id, type, priority, popup, published, expires, minimumVersion, maximumVersion);
    }

    private static IReadOnlyList<NewsContentBlock> ParseContent(string[] lines, int startIndex)
    {
        List<NewsContentBlock> blocks = new();
        HashSet<string> ids = new(StringComparer.Ordinal);
        int index = startIndex;

        while (index < lines.Length)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
            if (index >= lines.Length) break;

            Match directive = Regex.Match(lines[index], @"^(?<name>[A-Za-z][A-Za-z-]*):(?:\s*(?<value>.*))?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!directive.Success)
            {
                throw new InvalidDataException($"Invalid content line: {lines[index]}");
            }

            string name = directive.Groups["name"].Value.ToLowerInvariant();
            string inlineText = directive.Groups["value"].Value.Trim();
            index++;

            switch (name)
            {
                case "title":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Title, ReadText(lines, ref index, inlineText));
                    break;
                case "subtitle":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Subtitle, ReadText(lines, ref index, inlineText));
                    break;
                case "heading":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Heading, ReadText(lines, ref index, inlineText));
                    break;
                case "paragraph":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Paragraph, ReadText(lines, ref index, inlineText));
                    break;
                case "warning":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Warning, ReadText(lines, ref index, inlineText));
                    break;
                case "important":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Important, ReadText(lines, ref index, inlineText));
                    break;
                case "code":
                    AddSingleTextBlock(blocks, ids, NewsBlockType.Code, ReadText(lines, ref index, inlineText, preserveWhitespace: true));
                    break;
                case "list":
                    blocks.Add(new NewsContentBlock(NewsBlockType.BulletList, Items: ReadList(lines, ref index, numbered: false)));
                    break;
                case "numbered-list":
                    blocks.Add(new NewsContentBlock(NewsBlockType.NumberedList, Items: ReadList(lines, ref index, numbered: true)));
                    break;
                case "link":
                    blocks.Add(ParseLink(inlineText));
                    break;
                case "divider":
                    if (inlineText.Length != 0) throw new InvalidDataException("divider does not accept a value.");
                    blocks.Add(new NewsContentBlock(NewsBlockType.Divider));
                    break;
                default:
                    throw new InvalidDataException($"Unsupported news content element '{name}'.");
            }
        }

        return blocks;
    }

    private static string ReadText(string[] lines, ref int index, string inlineText, bool preserveWhitespace = false)
    {
        List<string> textLines = new();
        if (inlineText.Length > 0) textLines.Add(inlineText);
        while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) && !IsDirective(lines[index]))
        {
            textLines.Add(preserveWhitespace ? lines[index] : lines[index].Trim());
            index++;
        }

        string text = string.Join(Environment.NewLine, textLines);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("News content elements cannot be empty.");
        if (Regex.IsMatch(text, @"<\s*/?\s*[A-Za-z][^>]*>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new InvalidDataException("HTML markup is not supported in news content.");
        }

        return text;
    }

    private static IReadOnlyList<string> ReadList(string[] lines, ref int index, bool numbered)
    {
        List<string> items = new();
        while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
        {
            Match item = Regex.Match(lines[index], numbered ? @"^\s*\d+\.\s+(.+)$" : @"^\s*-\s+(.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!item.Success) break;
            items.Add(item.Groups[1].Value.Trim());
            index++;
        }

        if (items.Count == 0) throw new InvalidDataException("Lists must contain at least one item.");
        return items;
    }

    private static NewsContentBlock ParseLink(string value)
    {
        string[] parts = value.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
            !Uri.TryCreate(parts[1], UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidDataException("Links must use the format 'Label | https://example.com'.");
        }

        return new NewsContentBlock(NewsBlockType.Link, parts[0], Link: uri);
    }

    private static void AddSingleTextBlock(List<NewsContentBlock> blocks, HashSet<string> ids, NewsBlockType type, string text)
    {
        if (type == NewsBlockType.Title && !ids.Add("title"))
        {
            throw new InvalidDataException("News content must contain exactly one title.");
        }
        blocks.Add(new NewsContentBlock(type, text));
    }

    private static bool IsDirective(string line)
        => Regex.IsMatch(line, @"^[A-Za-z][A-Za-z-]*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : throw new InvalidDataException($"Required news metadata '{key}' is missing.");

    private static string? Optional(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value) ? value : null;

    private static DateTimeOffset ParseUtcTimestamp(string value, string key)
    {
        if (!value.EndsWith('Z') && !value.EndsWith("+00:00", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"News metadata '{key}' must be a UTC timestamp.");
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
        {
            throw new InvalidDataException($"News metadata '{key}' is not a valid timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    private static Version? ParseOptionalVersion(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = Regex.Replace(value.Trim(), @"[-+].*$", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        string[] parts = normalized.Split('.');
        if (parts.Length == 2) normalized += ".0";
        if (!Version.TryParse(normalized, out Version? version))
        {
            throw new InvalidDataException($"News metadata '{key}' is not a valid Pocket MC version.");
        }

        return version;
    }

    private static string ToEnumName(string value) => value.Replace('-', '_').Replace("_", "", StringComparison.Ordinal);
}