using System.Text.RegularExpressions;

namespace PocketMC.Application.Services.Players;

public static class CommandFormatter
{
    private static readonly Regex JavaPlayerNameRegex = new(
        "^[A-Za-z0-9_]{1,16}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static string FormatPlayerName(string name, string? serverType)
    {
        if (!IsValidPlayerName(name, serverType))
        {
            throw new ArgumentException("Player name contains characters that cannot be safely used in a server command.", nameof(name));
        }

        return FormatValidatedPlayerName(name, serverType);
    }

    public static bool TryFormatPlayerName(string name, string? serverType, out string formattedName)
    {
        formattedName = string.Empty;
        if (!IsValidPlayerName(name, serverType))
        {
            return false;
        }

        formattedName = FormatValidatedPlayerName(name, serverType);
        return true;
    }

    public static bool IsValidPlayerName(string name, string? serverType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (trimmed.Length > 100)
        {
            return false;
        }

        if (IsBedrock(serverType) || IsPocketMine(serverType))
        {
            return !trimmed.Any(character =>
                char.IsControl(character) ||
                character == '"' ||
                character == '\'' ||
                character == '\\');
        }

        return JavaPlayerNameRegex.IsMatch(trimmed);
    }

    private static string FormatValidatedPlayerName(string name, string? serverType)
    {
        string trimmed = name.Trim();
        bool isJava = !IsBedrock(serverType) && !IsPocketMine(serverType);

        if (isJava)
        {
            return trimmed;
        }

        if (IsBedrock(serverType) || IsPocketMine(serverType) || NeedsQuoting(trimmed))
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
