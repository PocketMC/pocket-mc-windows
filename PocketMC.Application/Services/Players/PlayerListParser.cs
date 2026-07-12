using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Players;

public sealed class PlayerListParseResult
{
    public PlayerListParseResult(
        int onlinePlayerCount,
        int? maxPlayers,
        IReadOnlyList<string> onlinePlayerNames,
        bool isComplete,
        PlayerListContinuationStyle continuationStyle = PlayerListContinuationStyle.None)
    {
        OnlinePlayerCount = onlinePlayerCount;
        MaxPlayers = maxPlayers;
        OnlinePlayerNames = onlinePlayerNames;
        IsComplete = isComplete;
        ContinuationStyle = continuationStyle;
    }

    public int OnlinePlayerCount { get; }
    public int? MaxPlayers { get; }
    public IReadOnlyList<string> OnlinePlayerNames { get; }
    public bool IsComplete { get; }
    public PlayerListContinuationStyle ContinuationStyle { get; }
}

public enum PlayerListContinuationStyle
{
    None,
    JavaDashedNames,
    BedrockPlainNames
}

public sealed class PlayerListParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex JavaInlineRegex = new(
        @"There\s+are\s+(?<count>\d+)\s+of\s+a\s+max\s+of\s+(?<max>\d+)\s+players\s+online:\s*(?<players>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex JavaMultilineHeaderRegex = new(
        @"There\s+are\s+(?<count>\d+)\s+players\s+online:\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BedrockInlineRegex = new(
        @"Players\s+connected\s+\((?<count>\d+)\s*/\s*(?<max>\d+)\):\s*(?<players>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BedrockHeaderRegex = new(
        @"There\s+are\s+(?<count>\d+)\s*/\s*(?<max>\d+)\s+players\s+online:\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BedrockTimestampRegex = new(
        @"^\[\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}:\d{3}\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex PocketMineInlineRegex = new(
        @"Online\s+players\s+\((?<count>\d+)\s*/\s*(?<max>\d+)\):\s*(?<players>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex CommandOutputPrefixRegex = new(
        @"^\s*Command\s+output\s*\|\s*(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static string NormalizePlayerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string normalized = name.Trim();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Match match = CommandOutputPrefixRegex.Match(normalized);
            if (!match.Success)
            {
                break;
            }

            normalized = match.Groups["name"].Value.Trim();
        }

        int pipeIndex = normalized.LastIndexOf('|');
        if (pipeIndex >= 0 &&
            normalized[..pipeIndex].Contains("Command output", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(pipeIndex + 1)..].Trim();
        }

        return normalized;
    }

    public PlayerListParseResult? ParseLine(string line, string? serverType)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        if (IsBedrock(serverType))
        {
            return TryParseBedrock(line) ?? TryParseAny(line);
        }

        if (IsPocketMine(serverType))
        {
            return TryParseInline(line, PocketMineInlineRegex) ?? TryParseAny(line);
        }

        return TryParseJava(line) ?? TryParseAny(line);
    }

    public bool TryParseContinuationLine(string line, out string playerName)
    {
        return TryParseContinuationLine(line, PlayerListContinuationStyle.JavaDashedNames, out playerName);
    }

    public bool TryParseContinuationLine(string line, PlayerListContinuationStyle style, out string playerName)
    {
        playerName = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (style == PlayerListContinuationStyle.BedrockPlainNames)
        {
            string plainName = line.Trim();
            if (BedrockTimestampRegex.IsMatch(plainName) || plainName.StartsWith("[20", StringComparison.Ordinal))
            {
                return false;
            }

            playerName = NormalizePlayerName(plainName);
            return true;
        }

        if (style != PlayerListContinuationStyle.JavaDashedNames)
        {
            return false;
        }

        string trimmed = line.Trim();
        int markerIndex = trimmed.IndexOf("- ", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        string prefix = trimmed[..markerIndex].Trim();
        bool prefixLooksLikeLogTag = prefix.Length == 0 ||
                                     prefix.EndsWith("]", StringComparison.Ordinal) ||
                                     prefix.EndsWith("]:", StringComparison.Ordinal) ||
                                     prefix.EndsWith(":", StringComparison.Ordinal);
        if (!prefixLooksLikeLogTag)
        {
            return false;
        }

        playerName = NormalizePlayerName(trimmed[(markerIndex + 2)..]);
        return !string.IsNullOrWhiteSpace(playerName);
    }

    private static PlayerListParseResult? TryParseAny(string line)
    {
        return TryParseJava(line)
            ?? TryParseBedrock(line)
            ?? TryParseInline(line, BedrockInlineRegex)
            ?? TryParseInline(line, PocketMineInlineRegex);
    }

    private static PlayerListParseResult? TryParseBedrock(string line)
    {
        PlayerListParseResult? inline = TryParseInline(line, BedrockInlineRegex);
        if (inline != null)
        {
            return inline;
        }

        Match header = BedrockHeaderRegex.Match(line);
        if (!header.Success ||
            !int.TryParse(header.Groups["count"].Value, out int count))
        {
            return null;
        }

        int? maxPlayers = int.TryParse(header.Groups["max"].Value, out int max)
            ? max
            : null;

        return new PlayerListParseResult(
            count,
            maxPlayers,
            Array.Empty<string>(),
            isComplete: count == 0,
            continuationStyle: count == 0 ? PlayerListContinuationStyle.None : PlayerListContinuationStyle.BedrockPlainNames);
    }

    private static PlayerListParseResult? TryParseJava(string line)
    {
        PlayerListParseResult? inline = TryParseInline(line, JavaInlineRegex);
        if (inline != null)
        {
            return inline;
        }

        Match multilineHeader = JavaMultilineHeaderRegex.Match(line);
        if (!multilineHeader.Success ||
            !int.TryParse(multilineHeader.Groups["count"].Value, out int count))
        {
            return null;
        }

        return new PlayerListParseResult(
            count,
            null,
            Array.Empty<string>(),
            count == 0,
            count == 0 ? PlayerListContinuationStyle.None : PlayerListContinuationStyle.JavaDashedNames);
    }

    private static PlayerListParseResult? TryParseInline(string line, Regex regex)
    {
        Match match = regex.Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups["count"].Value, out int count))
        {
            return null;
        }

        int? maxPlayers = int.TryParse(match.Groups["max"].Value, out int max)
            ? max
            : null;

        string playerSegment = match.Groups["players"].Value;
        return new PlayerListParseResult(
            count,
            maxPlayers,
            SplitPlayerNames(playerSegment),
            isComplete: true);
    }

    private static IReadOnlyList<string> SplitPlayerNames(string playerSegment)
    {
        if (string.IsNullOrWhiteSpace(playerSegment))
        {
            return Array.Empty<string>();
        }

        string[] names = playerSegment.Split(',', StringSplitOptions.RemoveEmptyEntries);
        List<string> parsed = new(names.Length);
        foreach (string name in names)
        {
            string trimmed = NormalizePlayerName(name);
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                parsed.Add(trimmed);
            }
        }

        return parsed;
    }
    private static bool IsBedrock(string? serverType) =>
        serverType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPocketMine(string? serverType) =>
        serverType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true ||
        serverType?.StartsWith("PocketMine", StringComparison.OrdinalIgnoreCase) == true;
}
