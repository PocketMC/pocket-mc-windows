using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Console
{
    public static class LogSanitizer
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        private static readonly Regex PlayitClaimUrlRegex = new(
            @"https://playit\.gg/claim/[A-Za-z0-9\-]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex SecretAssignmentRegex = new(
            @"\b(secret(?:[_-]?key)?|token)\b(\s*[:=]\s*)""?[^""\s,;]+""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex Ipv4Regex = new(
            @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
            RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex Ipv6Regex = new(
            @"(?<![\w:])(?:[A-F0-9]{1,4}:){1,7}:?(?:[A-F0-9]{1,4})?(?![\w:])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex EmailRegex = new(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        public static string SanitizeConsoleLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;

            string cleaned = SanitizeControlCharacters(line);
            
            // Protect player PII
            cleaned = Ipv4Regex.Replace(cleaned, "[REDACTED_IP]");
            cleaned = Ipv6Regex.Replace(cleaned, "[REDACTED_IP]");
            cleaned = EmailRegex.Replace(cleaned, "[REDACTED_EMAIL]");

            return cleaned;
        }

        private static string SanitizeControlCharacters(string line)
        {
            var builder = new StringBuilder(line.Length);
            foreach (char character in line)
            {
                if (!char.IsControl(character) || character == '\t')
                {
                    builder.Append(character);
                }
            }
            return builder.ToString();
        }

        public static string SanitizePlayitLine(string? line)
        {
            string sanitized = SanitizeConsoleLine(line);
            sanitized = PlayitClaimUrlRegex.Replace(sanitized, "https://playit.gg/claim/[REDACTED]");
            sanitized = SecretAssignmentRegex.Replace(
                sanitized,
                match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
            return sanitized;
        }
    }
}
