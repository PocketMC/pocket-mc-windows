using System;
using System.Net;
using System.Net.Sockets;
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

        private static readonly Regex Ipv6CandidateRegex = new(
            @"(?<![A-Za-z0-9])\[?(?<address>(?:[0-9A-Fa-f]{0,4}:){2,}[0-9A-Fa-f:.%]*)\]?(?![A-Za-z0-9])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex EmailRegex = new(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex AnsiEscapeRegex = new(
            @"\x1B\[[0-?]*[ -/]*[@-~]",
            RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex OrphanAnsiSgrRegex = new(
            @"\[(?:\d{1,3})(?:;\d{1,3})*m",
            RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex TimeOnlyRegex = new(
            @"^\d{1,2}:\d{2}:\d{2}$",
            RegexOptions.Compiled,
            RegexTimeout);

        public static string SanitizeConsoleLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;

            string cleaned = StripAnsi(line);
            cleaned = SanitizeControlCharacters(cleaned);

            cleaned = Ipv4Regex.Replace(cleaned, "[REDACTED_IP]");
            cleaned = RedactIpv6Tokens(cleaned);
            cleaned = EmailRegex.Replace(cleaned, "[REDACTED_EMAIL]");

            return cleaned;
        }

        private static string StripAnsi(string line)
        {
            string cleaned = AnsiEscapeRegex.Replace(line, string.Empty);
            return OrphanAnsiSgrRegex.Replace(cleaned, string.Empty);
        }

        private static string RedactIpv6Tokens(string line)
        {
            return Ipv6CandidateRegex.Replace(
                line,
                match =>
                {
                    string candidate = match.Groups["address"].Value;
                    return ShouldRedactIpv6(candidate) ? "[REDACTED_IP]" : match.Value;
                });
        }

        private static bool ShouldRedactIpv6(string candidate)
        {
            if (candidate.Count(static character => character == ':') < 2)
            {
                return false;
            }

            if (TimeOnlyRegex.IsMatch(candidate))
            {
                return false;
            }

            return IPAddress.TryParse(candidate, out IPAddress? address) &&
                address.AddressFamily == AddressFamily.InterNetworkV6;
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
