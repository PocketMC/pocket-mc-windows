using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using PocketMC.Domain.Models;
using PocketMC.Domain.Storage;
using PocketMC.Application.Services.Networking;
using PocketMC.Infrastructure.Marketplace;

namespace PocketMC.Infrastructure.Networking;

public class SimpleVoiceChatDetector : ISimpleVoiceChatDetector
{
    private readonly AddonManifestService _manifestService;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex VoiceChatStartedRegex = new(
        @"\[voicechat\]\s+Voice chat server started at port\s+(?<port>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    public SimpleVoiceChatDetector(AddonManifestService manifestService)
    {
        _manifestService = manifestService;
    }

    public SimpleVoiceChatDetection Detect(string? serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
        {
            return NotDetected();
        }

        SimpleVoiceChatDetectionSource source = SimpleVoiceChatDetectionSource.None;
        bool isDetected = false;

        // 1. Try manifest first
        var manifest = _manifestService.LoadManifest(serverDir);
        var entry = manifest.Entries.FirstOrDefault(e => 
            e.ProjectId == "9eGKb6K1" || 
            (e.ProjectTitle != null && e.ProjectTitle.Contains("Simple Voice Chat", StringComparison.OrdinalIgnoreCase)));

        if (entry != null)
        {
            isDetected = true;
            bool isPlugin = entry.Loader != null && (entry.Loader.Contains("bukkit", StringComparison.OrdinalIgnoreCase) || 
                                                     entry.Loader.Contains("spigot", StringComparison.OrdinalIgnoreCase) || 
                                                     entry.Loader.Contains("paper", StringComparison.OrdinalIgnoreCase));
            source = isPlugin ? SimpleVoiceChatDetectionSource.PluginJar : SimpleVoiceChatDetectionSource.ModJar;
        }

        // 2. Fallback to jar scan
        if (!isDetected)
        {
            bool hasModJar = TryFindVoiceChatJar(Path.Combine(serverDir, "mods"), out _);
            bool hasPluginJar = TryFindVoiceChatJar(Path.Combine(serverDir, "plugins"), out _);

            if (hasModJar || hasPluginJar)
            {
                isDetected = true;
                source = hasPluginJar ? SimpleVoiceChatDetectionSource.PluginJar : SimpleVoiceChatDetectionSource.ModJar;
            }
        }

        if (!isDetected)
        {
            return NotDetected();
        }

        if (SimpleVoiceChatConfigService.TryLoad(serverDir, out SimpleVoiceChatSettings settings))
        {
            return new SimpleVoiceChatDetection(
                true,
                source,
                settings.Port,
                settings.BindAddress,
                settings.VoiceHost,
                settings.ConfigPath,
                IsConfigPending: false);
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

        return Pending(source);
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
        yield return Path.Combine(logsDir, LogConstants.CurrentSessionLogName);
        yield return Path.Combine(logsDir, LogConstants.LastSessionLogName);
        yield return Path.Combine(logsDir, LogConstants.LegacySessionLogName);
    }
}
