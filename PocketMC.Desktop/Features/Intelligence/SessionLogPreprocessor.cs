using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Intelligence;

/// <summary>
/// Preprocesses raw Minecraft server logs, extracting meaningful events
/// and filtering noise to produce a concise log suitable for AI summarization.
/// </summary>
public static class SessionLogPreprocessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Lines matching these patterns are noise and should be removed
    private static readonly Regex[] NoisePatterns = new[]
    {
        CreateRegex(@"\[DEBUG\]", RegexOptions.IgnoreCase),
        CreateRegex(@"Can't keep up! Is the server overloaded\?"),
        CreateRegex(@"moved too quickly!"),
        CreateRegex(@"Preparing spawn area:"),
        CreateRegex(@"Loaded \d+ recipes"),
        CreateRegex(@"Loaded \d+ advancements"),
        CreateRegex(@"UUID of player"),
        CreateRegex(@"com\.mojang\.authlib"),
        CreateRegex(@"LoginListener"),
        CreateRegex(@"Chunk stats", RegexOptions.IgnoreCase),
    };

    // Lines matching these are important and should always be kept
    private static readonly Regex[] ImportantPatterns = new[]
    {
        CreateRegex(@"joined the game"),
        CreateRegex(@"left the game"),
        CreateRegex(@"was (slain|shot|blown|killed|drowned|burnt|squashed|fell|withered|poked|fireballed|stung|starved|suffocated|squished|impaled|frozen|struck)"),
        CreateRegex(@"\[Server\]"),
        CreateRegex(@"issued server command:"),
        CreateRegex(@"has (made the|completed) advancement"),
        CreateRegex(@"has reached the goal"),
        CreateRegex(@"(Stopping|Starting|Done \()"),
        CreateRegex(@"(ERROR|WARN|FATAL)", RegexOptions.IgnoreCase),
        CreateRegex(@"(kicked|banned|whitelisted|opped|de-opped)", RegexOptions.IgnoreCase),
        CreateRegex(@"Teleported"),
        CreateRegex(@"lost connection:"),
        CreateRegex(@"(challenge|has completed)", RegexOptions.IgnoreCase),
    };

    /// <summary>
    /// Maximum characters to send per AI request chunk.
    /// </summary>
    public const int MaxChunkChars = 80_000;

    /// <summary>
    /// Minimum number of meaningful lines in a session before summarization is worthwhile.
    /// </summary>
    public const int MinimumLines = 5;

    private static readonly Regex IpRegex = new(
        @"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b(?:[a-fA-F0-9]{1,4}:){2,7}[a-fA-F0-9]{1,4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static Regex CreateRegex(string pattern, RegexOptions options = RegexOptions.None)
    {
        return new Regex(pattern, options | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    }

    /// <summary>
    /// Process raw log lines into a cleaner, AI-friendly format.
    /// Returns null if the session is too short to summarize.
    /// </summary>
    public static string? Preprocess(string rawLog)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
            return null;

        var lines = rawLog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            // Redact IPs early
            line = IpRegex.Replace(line, "[REDACTED_IP]");

            // Skip noise
            if (NoisePatterns.Any(p => p.IsMatch(line)))
                continue;

            // Always keep important lines
            if (ImportantPatterns.Any(p => p.IsMatch(line)))
            {
                result.Add(line);
                continue;
            }

            // Keep general INFO-level lines that aren't noise
            if (line.Contains("[INFO]") || line.Contains("/INFO]"))
            {
                result.Add(line);
            }
        }

        if (result.Count < MinimumLines)
            return null;

        return string.Join('\n', result);
    }

    /// <summary>
    /// Split a large processed log into chunks that fit within the AI token limits.
    /// </summary>
    public static List<string> ChunkLog(string processedLog)
    {
        if (processedLog.Length <= MaxChunkChars)
            return new List<string> { processedLog };

        var chunks = new List<string>();
        var lines = processedLog.Split('\n');
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (current.Length + line.Length + 1 > MaxChunkChars && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            current.AppendLine(line);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }
}
