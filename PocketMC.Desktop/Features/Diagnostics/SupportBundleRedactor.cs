using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Diagnostics;

public sealed class SupportBundleRedactor
{
    private static readonly Regex[] SecretPatterns =
    {
        new(@"(?i)(api[_-]?key|access[_-]?token|refresh[_-]?token|agent[_-]?secret[_-]?key|client[_-]?secret|authorization|bearer)\s*[:=]\s*['"" ]?[^'""\r\n,}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"(?i)(Authorization:\s*Bearer\s+)[A-Za-z0-9._\-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"(?i)(dpapi:v1:)[A-Za-z0-9+/=]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        new(@"(?i)(playit[^\r\n]*(secret|token|agent)[^\r\n]*[:=]\s*)[^\r\n]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
    };

    private static readonly Regex EmailPattern = new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string redacted = input;
        foreach (var pattern in SecretPatterns)
        {
            redacted = pattern.Replace(redacted, match =>
            {
                string value = match.Value;
                int separatorIndex = Math.Max(value.LastIndexOf('='), value.LastIndexOf(':'));
                if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
                {
                    return "[REDACTED_SECRET]";
                }

                return value[..(separatorIndex + 1)] + " [REDACTED]";
            });
        }

        redacted = EmailPattern.Replace(redacted, "[REDACTED_EMAIL]");
        return redacted;
    }
}
