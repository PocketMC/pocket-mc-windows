using System;

namespace PocketMC.Desktop.Helpers;

public static class CommandFormatter
{
    public static string FormatPlayerName(string name, string? serverType)
    {
        string trimmed = name.Trim();
        if (IsBedrock(serverType))
        {
            return $"\"{trimmed.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return trimmed;
    }

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
