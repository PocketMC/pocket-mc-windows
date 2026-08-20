using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PocketMC.Application.Services.Players;

public sealed class PlayerGamemodeChangeEvent
{
    public PlayerGamemodeChangeEvent(string playerName, string gamemode, string rawLine)
    {
        PlayerName = playerName;
        Gamemode = gamemode;
        RawLine = rawLine;
    }

    public string PlayerName { get; }
    public string Gamemode { get; }
    public string RawLine { get; }
}

public static class PlayerGamemodeLogParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Strips ANSI escape sequences (e.g. \x1b[0;33;49m)
    private static readonly Regex AnsiRegex = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled,
        RegexTimeout);

    // Strips command output prefix
    private static readonly Regex CommandOutputPrefixRegex = new(
        @"^\s*Command\s+output\s*\|\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Strips server log prefixes: timestamps and standard logger brackets followed by optional colon
    // e.g. [12:34:56 INFO]: or [12:34:56] [Server thread/INFO]: or [2026-04-28 18:10:30:571 INFO]
    private static readonly Regex ServerLogPrefixRegex = new(
        @"^(?:\[\d{2,4}[-:\s\.\d]+(?:(?:\s+(?:INFO|WARN|ERROR|DEBUG|TRACE))|Z)?\]\s*:?\s*)*(?:\[(?:Server(?:\s+thread)?|main|Worker\S*|\w+)?\s*(?:/)?\s*(?:INFO|WARN|ERROR|DEBUG|TRACE)\]\s*:?\s*)?(?:\[(?:minecraft/)?\S+\]\s*:\s*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 1. Operator/Server setting target: "Set Steve's game mode to Creative Mode" or "[Server: Set Steve's game mode to Creative Mode]"
    private static readonly Regex SetTargetGamemodeRegex = new(
        @"^(?:\[[^:\]]+:\s*)?Set\s+(?<player>.+?)'s\s+game\s*mode\s+to\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)(?:\s+Mode)?(?:\s*\])?\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 2. Self gamemode change in-game: "[Steve: Set own game mode to Creative Mode]" or "Steve set own game mode to Creative Mode"
    private static readonly Regex SetOwnGamemodeRegex = new(
        @"^(?:\[(?<player>[^:\]]+):\s*Set\s+own\s+game\s*mode\s+to\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)(?:\s+Mode)?\]|(?<player>.+?)\s+set\s+own\s+game\s*mode\s+to\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)(?:\s+Mode)?)\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 3. Updated gamemode: "Steve's game mode has been updated to Creative Mode" or "Player Steve's game mode has been updated to Creative"
    private static readonly Regex GamemodeUpdatedRegex = new(
        @"^(?:Player\s+)?(?<player>.+?)'s\s+game\s*mode\s+(?:has\s+been\s+updated\s+to|was\s+changed\s+to)\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)(?:\s+Mode)?\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 4. Bedrock / PocketMine phrasing: "Game mode of Steve has been updated to Creative Mode" or "Game mode of player 'Steve' changed to 'creative'"
    private static readonly Regex GameModeOfPlayerRegex = new(
        @"^Game\s*mode\s+of\s+(?:player\s+)?['""]?(?<player>.+?)['""]?\s+(?:has\s+been\s+updated\s+to|changed\s+to|was\s+changed\s+to|set\s+to)\s+['""]?(?<mode>Survival|Creative|Adventure|Spectator|\w+)['""]?(?:\s+Mode)?\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 5. Essentials / Plugin phrasing: "[Essentials] Set game mode Creative for Steve" or "Set game mode creative for Steve"
    private static readonly Regex EssentialsSetGamemodeRegex = new(
        @"^(?:\[[^\]]+\]\s*)?Set\s+game\s*mode\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)\s+for\s+(?<player>.+?)\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 6. Generic Gamemode for <player> set to <mode>
    private static readonly Regex GamemodeForPlayerRegex = new(
        @"^(?:Gamemode|Game\s*mode)\s+for\s+(?<player>.+?)\s+set\s+to\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 7. Bedrock direct set: "Player Steve set game mode to Creative" or "Set Steve game mode to Creative"
    private static readonly Regex BedrockDirectSetRegex = new(
        @"^(?:Player\s+)?(?<player>.+?)\s+set\s+game\s*mode\s+to\s+(?<mode>Survival|Creative|Adventure|Spectator|\w+)(?:\s+Mode)?\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 8. Command issued by player: "Steve issued server command: /gamemode adventure [target]" or "Steve issued server command: /gma" or "Player Steve executed command: /gamemode 2"
    private static readonly Regex CommandIssuedGamemodeRegex = new(
        @"^(?:Player\s+)?(?<issuer>.+?)\s+(?:issued\s+server\s+command|executed\s+command):\s*/(?:game\s*mode|gm)\s+(?<mode>survival|creative|adventure|spectator|[0-3]|s|c|a|sp)(?:\s+(?<target>\S+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex CommandIssuedShorthandRegex = new(
        @"^(?:Player\s+)?(?<issuer>.+?)\s+(?:issued\s+server\s+command|executed\s+command):\s*/gm(?<mode>s|c|a|sp)(?:\s+(?<target>\S+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // 10. Query entity data response: "Steve has the following entity data: 2" or "Steve has the following entity data: 1b" or "[Server: Steve has the following entity data: 2]"
    private static readonly Regex EntityDataGamemodeRegex = new(
        @"^(?:\[[^:\]]+:\s*)?(?<player>[^\s:]+)\s+has\s+the\s+following\s+entity\s+data:\s*(?<mode>[0-3])[bdsf]?(?:\s*\])?\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    /// <summary>
    /// Ultra-fast fast-path check: returns false if the line cannot possibly contain a gamemode update.
    /// Runs in sub-microsecond time to prevent CPU load on high-throughput console lines.
    /// </summary>
    public static bool MightContainGamemode(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return line.Contains("mode", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("gamemode", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("entity data", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("/gm", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("/gma", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("/gmc", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("/gms", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("/gmsp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to parse a player gamemode change event from a raw server console output line.
    /// </summary>
    public static PlayerGamemodeChangeEvent? TryParse(string? rawLine)
    {
        if (!MightContainGamemode(rawLine))
        {
            return null;
        }

        string cleanLine = CleanLine(rawLine!);
        if (string.IsNullOrWhiteSpace(cleanLine))
        {
            return null;
        }

        // 1. Try Set target gamemode (e.g. Set Steve's game mode to Creative Mode)
        Match match = SetTargetGamemodeRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 2. Try Set own gamemode (e.g. [Steve: Set own game mode to Creative Mode])
        match = SetOwnGamemodeRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 3. Try Gamemode updated (e.g. Steve's game mode has been updated to Creative Mode)
        match = GamemodeUpdatedRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 4. Try Game mode of player (e.g. Game mode of Steve has been updated to Creative Mode)
        match = GameModeOfPlayerRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 5. Try Essentials / Plugin format (e.g. Set game mode Creative for Steve)
        match = EssentialsSetGamemodeRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 6. Try Gamemode for player (e.g. Gamemode for Steve set to Creative)
        match = GamemodeForPlayerRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 7. Try Bedrock direct set (e.g. Player Steve set game mode to Creative)
        match = BedrockDirectSetRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        // 8. Try command issued by player (e.g. Steve issued server command: /gamemode creative)
        match = CommandIssuedGamemodeRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateCommandEvent(match, rawLine!);
        }

        // 9. Try shorthand command issued (e.g. Steve issued server command: /gma)
        match = CommandIssuedShorthandRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateCommandEvent(match, rawLine!);
        }

        // 10. Try entity data query response (e.g. Steve has the following entity data: 2)
        match = EntityDataGamemodeRegex.Match(cleanLine);
        if (match.Success)
        {
            return CreateEvent(match, rawLine!);
        }

        return null;
    }

    /// <summary>
    /// Normalizes gamemode strings, numbers, or abbreviations to canonical values:
    /// survival, creative, adventure, spectator.
    /// </summary>
    public static string? NormalizeGamemode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        string trimmed = mode.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "0" or "s" or "survival" => "survival",
            "1" or "c" or "creative" => "creative",
            "2" or "a" or "adventure" => "adventure",
            "3" or "sp" or "spectator" => "spectator",
            _ => null
        };
    }

    private static string CleanLine(string line)
    {
        string text = line.Trim();
        if (text.Contains('\x1B'))
        {
            text = AnsiRegex.Replace(text, string.Empty).Trim();
        }

        text = CommandOutputPrefixRegex.Replace(text, string.Empty).Trim();

        // If line has standard server log prefixes, strip them:
        Match prefixMatch = ServerLogPrefixRegex.Match(text);
        if (prefixMatch.Success && prefixMatch.Length > 0)
        {
            // Only strip if the remaining text isn't empty
            string remaining = text[prefixMatch.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                text = remaining;
            }
        }

        return text;
    }

    private static PlayerGamemodeChangeEvent? CreateEvent(Match match, string rawLine)
    {
        string rawPlayer = match.Groups["player"].Value;
        string rawMode = match.Groups["mode"].Value;

        string playerName = PlayerListParser.NormalizePlayerName(rawPlayer);
        string? normalizedMode = NormalizeGamemode(rawMode);

        if (string.IsNullOrWhiteSpace(playerName) || normalizedMode == null)
        {
            return null;
        }

        return new PlayerGamemodeChangeEvent(playerName, normalizedMode, rawLine);
    }

    private static PlayerGamemodeChangeEvent? CreateCommandEvent(Match match, string rawLine)
    {
        string rawIssuer = match.Groups["issuer"].Value;
        string rawTarget = match.Groups["target"].Success ? match.Groups["target"].Value : string.Empty;
        string rawMode = match.Groups["mode"].Value;

        // If target is present, target player's gamemode changed; otherwise issuer's gamemode changed
        string effectivePlayer = !string.IsNullOrWhiteSpace(rawTarget) ? rawTarget : rawIssuer;
        string playerName = PlayerListParser.NormalizePlayerName(effectivePlayer);
        string? normalizedMode = NormalizeGamemode(rawMode);

        if (string.IsNullOrWhiteSpace(playerName) || normalizedMode == null)
        {
            return null;
        }

        return new PlayerGamemodeChangeEvent(playerName, normalizedMode, rawLine);
    }
}
