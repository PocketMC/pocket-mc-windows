using PocketMC.Desktop.Features.Console;
using System;

namespace PocketMC.Desktop.Features.Console;

public static class CommandFormatter
{
    public static string FormatPlayerName(string name, string? serverType)
    {
        string trimmed = name.Trim();
        bool isJava = !IsBedrock(serverType) && !IsPocketMine(serverType);

        if (isJava)
        {
            return trimmed;
        }

        if (IsBedrock(serverType) || NeedsQuoting(trimmed))
        {
            return Quote(trimmed);
        }

        return trimmed;
    }

    private static bool NeedsQuoting(string name)
    {
        foreach (char character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return true;
            }
        }

        return false;
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    public static string SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        return reason
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    public static string AppendOptionalReason(string command, string? reason)
    {
        string sanitized = SanitizeReason(reason);
        return string.IsNullOrWhiteSpace(sanitized)
            ? command
            : $"{command} {sanitized}";
    }

    public static bool IsBedrock(string? serverType) =>
        serverType?.StartsWith("Bedrock", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsPocketMine(string? serverType) =>
        serverType?.StartsWith("Pocketmine", StringComparison.OrdinalIgnoreCase) == true ||
        serverType?.StartsWith("PocketMine", StringComparison.OrdinalIgnoreCase) == true;
}
