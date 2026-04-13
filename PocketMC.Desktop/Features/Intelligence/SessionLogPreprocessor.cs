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
    // Lines matching these patterns are noise and should be removed
    private static readonly Regex[] NoisePatterns = new[]
    {
        new Regex(@"\[DEBUG\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"Can't keep up! Is the server overloaded\?", RegexOptions.Compiled),
        new Regex(@"moved too quickly!", RegexOptions.Compiled),
        new Regex(@"Preparing spawn area:", RegexOptions.Compiled),
        new Regex(@"Loaded \d+ recipes", RegexOptions.Compiled),
        new Regex(@"Loaded \d+ advancements", RegexOptions.Compiled),
        new Regex(@"UUID of player", RegexOptions.Compiled),
        new Regex(@"com\.mojang\.authlib", RegexOptions.Compiled),
        new Regex(@"LoginListener", RegexOptions.Compiled),
        new Regex(@"Chunk stats", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Lines matching these are important and should always be kept
    private static readonly Regex[] ImportantPatterns = new[]
    {
        new Regex(@"joined the game", RegexOptions.Compiled),
        new Regex(@"left the game", RegexOptions.Compiled),
        new Regex(@"was (slain|shot|blown|killed|drowned|burnt|squashed|fell|withered|poked|fireballed|stung|starved|suffocated|squished|impaled|frozen|struck)", RegexOptions.Compiled),
        new Regex(@"\[Server\]", RegexOptions.Compiled),
        new Regex(@"issued server command:", RegexOptions.Compiled),
        new Regex(@"has (made the|completed) advancement", RegexOptions.Compiled),
        new Regex(@"has reached the goal", RegexOptions.Compiled),
        new Regex(@"(Stopping|Starting|Done \()", RegexOptions.Compiled),
        new Regex(@"(ERROR|WARN|FATAL)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"(kicked|banned|whitelisted|opped|de-opped)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"Teleported", RegexOptions.Compiled),
        new Regex(@"lost connection:", RegexOptions.Compiled),
        new Regex(@"(challenge|has completed)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    /// <summary>
    /// Maximum characters to send per AI request chunk.
    /// </summary>
    public const int MaxChunkChars = 80_000;

    /// <summary>
    /// Minimum number of meaningful lines in a session before summarization is worthwhile.
    /// </summary>
    public const int MinimumLines = 5;

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
