using System;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Console;

public static class LogLineClassifier
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex ChatOrPlayerEventRegex = new(
        @"^\[.*?\](?:\s*\[.*?\])*:\s*(?:<[^>]+>\s.*|[^\s:]+\s(?:joined|left) the game)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex LogPrefixRegex = new(
        @"^\[.*?\](?:\s*\[.*?\])*:\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static LogLevel Classify(string? text, LogLevel previousLevel)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LogLevel.Info;
        }

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith("at ", StringComparison.Ordinal) ||
            trimmed.StartsWith("...", StringComparison.Ordinal) ||
            text.Contains("Caused by:", StringComparison.Ordinal))
        {
            return previousLevel;
        }

        if (text.StartsWith("> ", StringComparison.Ordinal))
        {
            return LogLevel.System;
        }

        if (IsChatOrPlayerEvent(text))
        {
            return LogLevel.Chat;
        }

        string searchTarget = text.ToUpperInvariant();
        try
        {
            Match prefixMatch = LogPrefixRegex.Match(text);
            if (prefixMatch.Success)
            {
                searchTarget = prefixMatch.Value.ToUpperInvariant();
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Fallback to full text if regex times out
        }

        if (ContainsAny(searchTarget, "ERROR", "FATAL", "SEVERE", "CRITICAL", "EXCEPTION"))
        {
            return LogLevel.Error;
        }

        if (ContainsAny(searchTarget, "WARN", "****", "***"))
        {
            return LogLevel.Warn;
        }

        if (ContainsAny(searchTarget, "DEBUG"))
        {
            return LogLevel.Debug;
        }

        if (ContainsAny(searchTarget, "TRACE"))
        {
            return LogLevel.Trace;
        }

        if (text.Contains("Done (", StringComparison.Ordinal) ||
            text.Contains("Server started", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Info; // Could add a Success level later, but Info is fine
        }

        return LogLevel.Info;
    }

    private static bool IsChatOrPlayerEvent(string text)
    {
        try
        {
            return ChatOrPlayerEventRegex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
