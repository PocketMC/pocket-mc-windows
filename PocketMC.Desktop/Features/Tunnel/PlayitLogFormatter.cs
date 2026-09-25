using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.Tunnel
{
    public enum PlayitLogLevel
    {
        Trace,
        Debug,
        Info,
        Success,
        Warn,
        Error
    }

    public sealed class PlayitLogEntry
    {
        public DateTime? Timestamp { get; set; }
        public string TimeText { get; set; } = string.Empty;
        public PlayitLogLevel Level { get; set; } = PlayitLogLevel.Info;
        public string LevelBadge { get; set; } = "INFO";
        public string Module { get; set; } = "Agent";
        public string Message { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;
        public int RepeatCount { get; set; } = 1;
        public bool IsTransientRetry { get; set; }

        public string GetSearchableText()
        {
            return $"{TimeText} {LevelBadge} {Module} {Message} {RawLine}";
        }
    }

    public static class PlayitLogFormatter
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        // Pattern matching outer timestamp from file: [2026-09-22 16:37:11] STDOUT: ...
        private static readonly Regex FilePrefixRegex = new(
            @"^\[(?<fileTime>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\]\s+(?:(?:STDOUT|STDERR):\s+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            RegexTimeout);

        // Pattern matching standard Rust tracing output: 2026-09-22T16:37:11.617652Z ERROR playit_agent_core::agent_control::connected_control: msg
        private static readonly Regex RustTracingRegex = new(
            @"^(?<isoTime>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?)\s+(?<level>[A-Z]+)\s+(?<module>[a-zA-Z0-9_:]+):\s+(?<msg>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            RegexTimeout);

        // Pattern matching simple process output like: INFO: playit.exe started
        private static readonly Regex SimplePrefixRegex = new(
            @"^(?<level>INFO|WARN|ERROR|DEBUG|TRACE):\s+(?<msg>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex TunnelCountRegex = new(
            @"tunnel_count=(?<count>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        private static readonly Regex AccountStatusRegex = new(
            @"account_status=""?(?<status>[^""\s,;]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);

        public static PlayitLogEntry Parse(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return new PlayitLogEntry
                {
                    TimeText = DateTime.Now.ToString("HH:mm:ss"),
                    Level = PlayitLogLevel.Info,
                    LevelBadge = "INFO",
                    Module = "Agent",
                    Message = string.Empty,
                    RawLine = rawLine ?? string.Empty
                };
            }

            string line = rawLine.Trim();
            string? fallbackTime = null;

            // 1. Strip file prefix if present
            var fileMatch = FilePrefixRegex.Match(line);
            if (fileMatch.Success)
            {
                fallbackTime = fileMatch.Groups["fileTime"].Value;
                line = line[fileMatch.Length..].Trim();
            }

            // 2. Check for standard Rust tracing format
            var rustMatch = RustTracingRegex.Match(line);
            if (rustMatch.Success)
            {
                string isoTime = rustMatch.Groups["isoTime"].Value;
                string rawLevel = rustMatch.Groups["level"].Value.ToUpperInvariant();
                string rawModule = rustMatch.Groups["module"].Value;
                string msg = rustMatch.Groups["msg"].Value.Trim();

                string timeText = FormatTimestamp(isoTime, fallbackTime);
                string cleanModule = SimplifyModule(rawModule);

                var entry = new PlayitLogEntry
                {
                    TimeText = timeText,
                    Module = cleanModule,
                    RawLine = rawLine
                };

                ClassifyMessage(entry, rawLevel, msg);
                return entry;
            }

            // 3. Check for simple prefix format (e.g. INFO: playit.exe started)
            var simpleMatch = SimplePrefixRegex.Match(line);
            if (simpleMatch.Success)
            {
                string rawLevel = simpleMatch.Groups["level"].Value.ToUpperInvariant();
                string msg = simpleMatch.Groups["msg"].Value.Trim();

                string timeText = FormatTimestamp(null, fallbackTime);
                var entry = new PlayitLogEntry
                {
                    TimeText = timeText,
                    Module = "Agent",
                    RawLine = rawLine
                };

                ClassifyMessage(entry, rawLevel, msg);
                return entry;
            }

            // 4. Fallback unstructured line
            string defTime = FormatTimestamp(null, fallbackTime);
            var fallbackEntry = new PlayitLogEntry
            {
                TimeText = defTime,
                Module = "Agent",
                RawLine = rawLine
            };

            ClassifyMessage(fallbackEntry, "INFO", line);
            return fallbackEntry;
        }

        private static string FormatTimestamp(string? isoTime, string? fallbackTime)
        {
            if (!string.IsNullOrEmpty(isoTime) &&
                DateTimeOffset.TryParse(isoTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto))
            {
                return dto.LocalDateTime.ToString("HH:mm:ss");
            }

            if (!string.IsNullOrEmpty(fallbackTime) && fallbackTime.Length >= 19)
            {
                // Fallback time is "yyyy-MM-dd HH:mm:ss"
                return fallbackTime.Substring(11, 8);
            }

            return DateTime.Now.ToString("HH:mm:ss");
        }

        private static string SimplifyModule(string rawModule)
        {
            if (string.IsNullOrWhiteSpace(rawModule)) return "Agent";
            if (rawModule.Contains("connected_control", StringComparison.OrdinalIgnoreCase)) return "Control";
            if (rawModule.Contains("maintained_control", StringComparison.OrdinalIgnoreCase)) return "Session";
            if (rawModule.Contains("daemon", StringComparison.OrdinalIgnoreCase)) return "Daemon";
            if (rawModule.Contains("tunnel", StringComparison.OrdinalIgnoreCase)) return "Tunnel";
            if (rawModule.Contains("lan", StringComparison.OrdinalIgnoreCase)) return "LAN";
            if (rawModule.Contains("rpc", StringComparison.OrdinalIgnoreCase)) return "RPC";

            int lastColon = rawModule.LastIndexOf("::", StringComparison.Ordinal);
            if (lastColon >= 0 && lastColon < rawModule.Length - 2)
            {
                string sub = rawModule.Substring(lastColon + 2);
                if (sub.Length > 0)
                {
                    return char.ToUpperInvariant(sub[0]) + sub.Substring(1);
                }
            }

            return rawModule;
        }

        private static void ClassifyMessage(PlayitLogEntry entry, string rawLevel, string message)
        {
            // 1. Transient registration timeout: Playit logs this as ERROR whenever a keepalive or register
            // UDP packet experiences minor delay/jitter. It is a transient retry, not a fatal failure.
            if (message.Contains("timeout waiting for register response", StringComparison.OrdinalIgnoreCase))
            {
                entry.Level = PlayitLogLevel.Warn;
                entry.LevelBadge = "RETRY";
                entry.IsTransientRetry = true;
                entry.Message = "Handshake timeout waiting for register response (reconnecting...)";
                return;
            }

            // 2. Control session reconnecting
            if (message.Contains("control session expired; reconnecting", StringComparison.OrdinalIgnoreCase))
            {
                entry.Level = PlayitLogLevel.Warn;
                entry.LevelBadge = "WARN";
                entry.Message = "Control session expired; renewing session (reason: SessionNotSetup)";
                return;
            }

            // 3. Successful tunnel connection
            if (message.Contains("playit connected", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("tunnels loaded", StringComparison.OrdinalIgnoreCase))
            {
                entry.Level = PlayitLogLevel.Success;
                entry.LevelBadge = "READY";

                var countMatch = TunnelCountRegex.Match(message);
                var statusMatch = AccountStatusRegex.Match(message);

                if (countMatch.Success && statusMatch.Success)
                {
                    entry.Message = $"Playit connected! Loaded {countMatch.Groups["count"].Value} tunnels (Account: {statusMatch.Groups["status"].Value})";
                }
                else if (countMatch.Success)
                {
                    entry.Message = $"Playit connected! Loaded {countMatch.Groups["count"].Value} tunnels.";
                }
                else
                {
                    entry.Message = "Playit tunnel connected successfully.";
                }
                return;
            }

            // 4. General level classification
            switch (rawLevel)
            {
                case "ERROR":
                    entry.Level = PlayitLogLevel.Error;
                    entry.LevelBadge = "ERROR";
                    break;
                case "WARN":
                case "WARNING":
                    entry.Level = PlayitLogLevel.Warn;
                    entry.LevelBadge = "WARN";
                    break;
                case "SUCCESS":
                    entry.Level = PlayitLogLevel.Success;
                    entry.LevelBadge = "READY";
                    break;
                case "DEBUG":
                    entry.Level = PlayitLogLevel.Debug;
                    entry.LevelBadge = "DEBUG";
                    break;
                case "TRACE":
                    entry.Level = PlayitLogLevel.Trace;
                    entry.LevelBadge = "TRACE";
                    break;
                default:
                    entry.Level = PlayitLogLevel.Info;
                    entry.LevelBadge = "INFO";
                    break;
            }

            entry.Message = message;
        }

        public static bool CanMerge(PlayitLogEntry? prev, PlayitLogEntry current)
        {
            if (prev == null || current == null) return false;

            // Only collapse consecutive transient retries or identical consecutive warnings
            if (!prev.IsTransientRetry && !current.IsTransientRetry && prev.Level != PlayitLogLevel.Warn)
            {
                return false;
            }

            return prev.Level == current.Level &&
                   string.Equals(prev.Module, current.Module, StringComparison.Ordinal) &&
                   string.Equals(prev.Message, current.Message, StringComparison.Ordinal);
        }

        public static bool MatchesSearch(PlayitLogEntry entry, string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;

            string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string searchable = entry.GetSearchableText();

            foreach (string token in tokens)
            {
                if (token.StartsWith('-') && token.Length > 1)
                {
                    if (searchable.Contains(token[1..], StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else if (!searchable.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
