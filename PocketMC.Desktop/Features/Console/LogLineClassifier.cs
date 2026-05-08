using System;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Console;

public static class LogLineClassifier
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex ChatOrPlayerEventRegex = new(
        @"^\[\d{2}:\d{2}:\d{2}\sINFO\]:\s(?:<[^>]+>\s.*|[^\s:]+\s(?:joined|left) the game|\[[^\]]+\]\s.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static LogLevel Classify(string? text, LogLevel previousLevel)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LogLevel.Info;
        }

        if (ContainsAny(text, "/ERROR]", "[ERROR]", " ERROR]", "Exception", "Fatal", "FATAL", "Error:", " SEVERE", " CRITICAL"))
        {
            return LogLevel.Error;
        }

        if (ContainsAny(text, "/WARN]", "[WARN]", " WARN]", " WARN", "Warning", "WARNING", "****", "***"))
        {
            return LogLevel.Warn;
        }

        if (ContainsAny(text, "/DEBUG]", "[DEBUG]", " DEBUG]"))
        {
            return LogLevel.Debug;
        }

        if (ContainsAny(text, "/TRACE]", "[TRACE]", " TRACE]"))
        {
            return LogLevel.Trace;
        }

        if (text.Contains("Done (", StringComparison.Ordinal) ||
            text.Contains("Server started", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Info;
        }

        if (IsChatOrPlayerEvent(text))
        {
            return LogLevel.Chat;
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
