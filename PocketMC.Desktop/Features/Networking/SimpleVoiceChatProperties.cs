using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PocketMC.Desktop.Infrastructure.FileSystem;

namespace PocketMC.Desktop.Features.Networking;

public sealed record SimpleVoiceChatSettings(
    string ConfigPath,
    int Port,
    string BindAddress,
    string? VoiceHost);

public enum SimpleVoiceChatDetectionSource
{
    None = 0,
    ConfigFile,
    ModJar,
    PluginJar,
    Log
}

public sealed record SimpleVoiceChatDetection(
    bool IsDetected,
    SimpleVoiceChatDetectionSource Source,
    int Port,
    string BindAddress,
    string? VoiceHost,
    string? ConfigPath,
    bool IsConfigPending);

public static class SimpleVoiceChatDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex VoiceChatStartedRegex = new(
        @"\[voicechat\]\s+Voice chat server started at port\s+(?<port>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static SimpleVoiceChatDetection Detect(string? serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
        {
            return NotDetected();
        }

        if (SimpleVoiceChatConfigService.TryLoad(serverDir, out SimpleVoiceChatSettings settings))
        {
            return new SimpleVoiceChatDetection(
                true,
                SimpleVoiceChatDetectionSource.ConfigFile,
                settings.Port,
                settings.BindAddress,
                settings.VoiceHost,
                settings.ConfigPath,
                IsConfigPending: false);
        }

        if (TryFindVoiceChatJar(Path.Combine(serverDir, "mods"), out _))
        {
            return Pending(SimpleVoiceChatDetectionSource.ModJar);
        }

        if (TryFindVoiceChatJar(Path.Combine(serverDir, "plugins"), out _))
        {
            return Pending(SimpleVoiceChatDetectionSource.PluginJar);
        }

        if (TryFindVoiceChatLogPort(serverDir, out int logPort))
        {
            return new SimpleVoiceChatDetection(
                true,
                SimpleVoiceChatDetectionSource.Log,
                logPort,
                SimpleVoiceChatConfigService.DefaultBindAddress,
                VoiceHost: null,
                ConfigPath: null,
                IsConfigPending: true);
        }

        return NotDetected();
    }

    private static SimpleVoiceChatDetection Pending(SimpleVoiceChatDetectionSource source)
    {
        return new SimpleVoiceChatDetection(
            true,
            source,
            SimpleVoiceChatConfigService.DefaultPort,
            SimpleVoiceChatConfigService.DefaultBindAddress,
            VoiceHost: null,
            ConfigPath: null,
            IsConfigPending: true);
    }

    private static SimpleVoiceChatDetection NotDetected()
    {
        return new SimpleVoiceChatDetection(
            false,
            SimpleVoiceChatDetectionSource.None,
            SimpleVoiceChatConfigService.DefaultPort,
            SimpleVoiceChatConfigService.DefaultBindAddress,
            VoiceHost: null,
            ConfigPath: null,
            IsConfigPending: false);
    }

    private static bool TryFindVoiceChatJar(string directory, out string? path)
    {
        path = null;
        if (!Directory.Exists(directory))
        {
            return false;
        }

        foreach (string jarPath in Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(jarPath);
            if (IsBaseSimpleVoiceChatJar(name))
            {
                path = jarPath;
                return true;
            }
        }

        return false;
    }

    private static bool IsBaseSimpleVoiceChatJar(string name)
    {
        return name.StartsWith("voicechat-", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("simplevoicechat-", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("simple-voice-chat-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindVoiceChatLogPort(string serverDir, out int port)
    {
        port = SimpleVoiceChatConfigService.DefaultPort;
        string logsDir = Path.Combine(serverDir, "logs");
        if (!Directory.Exists(logsDir))
        {
            return false;
        }

        foreach (string logPath in EnumerateLogFallbackPaths(logsDir))
        {
            if (!File.Exists(logPath))
            {
                continue;
            }

            foreach (string line in ReadTailLines(logPath, 500).Reverse())
            {
                Match match = VoiceChatStartedRegex.Match(line);
                if (match.Success &&
                    int.TryParse(match.Groups["port"].Value, out int parsedPort))
                {
                    port = parsedPort;
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> ReadTailLines(string logPath, int maxLines)
    {
        var tail = new Queue<string>(maxLines);

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == maxLines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        return tail;
    }

    private static IEnumerable<string> EnumerateLogFallbackPaths(string logsDir)
    {
        yield return Path.Combine(logsDir, "latest.log");
        yield return Path.Combine(logsDir, "pocketmc-session.log");
    }
}

public static class SimpleVoiceChatConfigService
{
    public const int DefaultPort = 24454;
    public const string DefaultBindAddress = "*";
    private const string StableNewline = "\r\n";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static string DetectConfigPath(string instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            throw new ArgumentException("Instance path is required.", nameof(instancePath));
        }

        return ResolveConfigPath(instancePath)
            ?? Path.Combine(instancePath, "config", "voicechat", "voicechat-server.properties");
    }

    public static string DetectConfigPath(string instancePath, SimpleVoiceChatDetectionSource detectionSource)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            throw new ArgumentException("Instance path is required.", nameof(instancePath));
        }

        string? existing = ResolveConfigPath(instancePath);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return GetDefaultConfigPath(instancePath, detectionSource);
    }

    public static SimpleVoiceChatSettings ReadConfig(string instancePath)
    {
        string configPath = DetectConfigPath(instancePath);
        return File.Exists(configPath)
            ? Read(configPath)
            : new SimpleVoiceChatSettings(configPath, DefaultPort, DefaultBindAddress, null);
    }

    public static string CreateInitialConfig(string instancePath, int port, string voiceHost)
    {
        return CreateInitialConfig(instancePath, port, voiceHost, SimpleVoiceChatDetectionSource.ModJar);
    }

    public static string CreateInitialConfig(
        string instancePath,
        int port,
        string voiceHost,
        SimpleVoiceChatDetectionSource detectionSource)
    {
        string configPath = DetectConfigPath(instancePath, detectionSource);
        string? parent = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        string content = string.Join(
            StableNewline,
            "# Generated by PocketMC for Simple Voice Chat Playit tunnel support.",
            $"port={NormalizePort(port)}",
            "bind_address=*",
            $"voice_host={voiceHost}",
            string.Empty);
        FileUtils.AtomicWriteAllText(configPath, content);
        return configPath;
    }

    private static string GetDefaultConfigPath(string instancePath, SimpleVoiceChatDetectionSource detectionSource)
    {
        return detectionSource == SimpleVoiceChatDetectionSource.PluginJar
            ? Path.Combine(instancePath, "plugins", "voicechat", "voicechat-server.properties")
            : Path.Combine(instancePath, "config", "voicechat", "voicechat-server.properties");
    }

    public static bool PatchVoiceHost(string configPath, string voiceHost)
    {
        return TryPatchVoiceHost(configPath, voiceHost);
    }

    public static bool PatchPortIfNeeded(string configPath, int port)
    {
        return PatchProperty(configPath, "port", NormalizePort(port).ToString(), appendIfMissing: true);
    }

    public static bool TryLoad(string? serverDir, out SimpleVoiceChatSettings settings)
    {
        settings = new SimpleVoiceChatSettings(string.Empty, DefaultPort, DefaultBindAddress, null);

        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
        {
            return false;
        }

        foreach (string configPath in EnumerateConfigPaths(serverDir))
        {
            if (!File.Exists(configPath))
            {
                continue;
            }

            settings = Read(configPath);
            return true;
        }

        return false;
    }

    public static bool TryPatchVoiceHost(string? serverDirOrConfigPath, string publicAddress)
    {
        if (string.IsNullOrWhiteSpace(serverDirOrConfigPath) || string.IsNullOrWhiteSpace(publicAddress))
        {
            return false;
        }

        string? configPath = ResolveConfigPath(serverDirOrConfigPath);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return false;
        }

        return PatchProperty(configPath, "voice_host", publicAddress, appendIfMissing: true);
    }

    private static bool PatchProperty(string configPath, string key, string value, bool appendIfMissing)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return false;
        }

        string text = File.ReadAllText(configPath);
        string newline = DetectNewline(text);
        bool hadFinalNewline = EndsWithNewline(text);
        string[] lines = SplitLines(text);
        var matchingIndexes = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (TryParseLine(lines[i], out ParsedProperty property) &&
                property.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                matchingIndexes.Add(i);
            }
        }

        string[] originalLines = lines.ToArray();
        if (matchingIndexes.Count == 0)
        {
            if (!appendIfMissing)
            {
                return false;
            }

            var updated = lines.ToList();
            updated.Add($"{key}={value}");
            lines = updated.ToArray();
        }
        else
        {
            int lastMatchIndex = matchingIndexes[^1];
            var updated = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (matchingIndexes.Contains(i) && i != lastMatchIndex)
                {
                    continue;
                }

                if (i == lastMatchIndex &&
                    TryParseLine(lines[i], out ParsedProperty property))
                {
                    updated.Add($"{property.Prefix}{value}{property.Suffix}");
                    continue;
                }

                updated.Add(lines[i]);
            }

            lines = updated.ToArray();
        }

        if (originalLines.SequenceEqual(lines))
        {
            return false;
        }

        FileUtils.AtomicWriteAllText(configPath, string.Join(newline, lines) + (hadFinalNewline ? newline : string.Empty));
        return true;
    }

    private static int NormalizePort(int port)
    {
        return port is >= 1 and <= 65535 ? port : DefaultPort;
    }

    private static SimpleVoiceChatSettings Read(string configPath)
    {
        int port = DefaultPort;
        string bindAddress = DefaultBindAddress;
        string? voiceHost = null;

        foreach (string line in File.ReadLines(configPath))
        {
            if (!TryParseLine(line, out ParsedProperty property))
            {
                continue;
            }

            if (property.Key.Equals("port", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(property.Value, out int parsedPort))
            {
                port = parsedPort;
            }
            else if (property.Key.Equals("bind_address", StringComparison.OrdinalIgnoreCase))
            {
                bindAddress = string.IsNullOrWhiteSpace(property.Value)
                    ? DefaultBindAddress
                    : property.Value;
            }
            else if (property.Key.Equals("voice_host", StringComparison.OrdinalIgnoreCase))
            {
                voiceHost = property.Value;
            }
        }

        return new SimpleVoiceChatSettings(configPath, port, bindAddress, voiceHost);
    }

    private static IEnumerable<string> EnumerateConfigPaths(string serverDir)
    {
        yield return Path.Combine(serverDir, "config", "voicechat", "voicechat-server.properties");
        yield return Path.Combine(serverDir, "plugins", "voicechat", "voicechat-server.properties");
        yield return Path.Combine(serverDir, "config", "simplevoicechat", "voicechat-server.properties");
    }

    private static string? ResolveConfigPath(string serverDirOrConfigPath)
    {
        if (File.Exists(serverDirOrConfigPath))
        {
            return serverDirOrConfigPath;
        }

        if (!Directory.Exists(serverDirOrConfigPath))
        {
            return null;
        }

        return EnumerateConfigPaths(serverDirOrConfigPath).FirstOrDefault(File.Exists);
    }

    private static bool TryParseLine(string line, out ParsedProperty property)
    {
        property = default;
        string trimmedStart = line.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmedStart) ||
            trimmedStart.StartsWith("#", StringComparison.Ordinal) ||
            trimmedStart.StartsWith("!", StringComparison.Ordinal))
        {
            return false;
        }

        int separatorIndex = FindSeparatorIndex(line);
        if (separatorIndex < 0)
        {
            return false;
        }

        string key = line[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        int valueStart = separatorIndex + 1;
        while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
        {
            valueStart++;
        }

        string valueAndSuffix = line[valueStart..];
        string value = StripInlineComment(valueAndSuffix).Trim();
        string suffix = valueAndSuffix[value.Length..];
        property = new ParsedProperty(key, value, line[..valueStart], suffix);
        return true;
    }

    private static int FindSeparatorIndex(string line)
    {
        int equalsIndex = line.IndexOf('=');
        int colonIndex = line.IndexOf(':');

        if (equalsIndex < 0)
        {
            return colonIndex;
        }

        if (colonIndex < 0)
        {
            return equalsIndex;
        }

        return Math.Min(equalsIndex, colonIndex);
    }

    private static string StripInlineComment(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if ((value[i] == '#' || value[i] == '!') &&
                (i == 0 || char.IsWhiteSpace(value[i - 1])))
            {
                return value[..i];
            }
        }

        return value;
    }

    private static string DetectNewline(string text)
    {
        int crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
        if (crlf >= 0)
        {
            return "\r\n";
        }

        int lf = text.IndexOf('\n');
        if (lf >= 0)
        {
            return "\n";
        }

        int cr = text.IndexOf('\r');
        return cr >= 0 ? "\r" : Environment.NewLine;
    }

    private static bool EndsWithNewline(string text)
    {
        return text.EndsWith("\r\n", StringComparison.Ordinal) ||
               text.EndsWith("\n", StringComparison.Ordinal) ||
               text.EndsWith("\r", StringComparison.Ordinal);
    }

    private static string[] SplitLines(string text)
    {
        string[] lines = Regex.Split(text, "\r\n|\n|\r", RegexOptions.CultureInvariant, RegexTimeout);
        if (lines.Length > 0 && lines[^1].Length == 0 && EndsWithNewline(text))
        {
            return lines[..^1];
        }

        return lines;
    }

    private readonly record struct ParsedProperty(
        string Key,
        string Value,
        string Prefix,
        string Suffix);
}

public static class SimpleVoiceChatProperties
{
    public const int DefaultPort = SimpleVoiceChatConfigService.DefaultPort;
    public const string DefaultBindAddress = SimpleVoiceChatConfigService.DefaultBindAddress;

    public static bool TryLoad(string? serverDir, out SimpleVoiceChatSettings settings)
        => SimpleVoiceChatConfigService.TryLoad(serverDir, out settings);

    public static bool TryPatchVoiceHost(string? serverDirOrConfigPath, string publicAddress)
        => SimpleVoiceChatConfigService.TryPatchVoiceHost(serverDirOrConfigPath, publicAddress);
}
