using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Players;

public sealed class BedrockBanEntry
{
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("xuid")]
    public string Xuid { get; set; } = string.Empty;

    public DateTime BannedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BannedBy { get; set; } = "console";
}

public sealed class BedrockPlayerDataService
{
    private static readonly Regex ConnectionRegex = new(
        @"Player\s+connected:\s*(?<name>.+?),\s*xuid:\s*(?<xuid>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private const string PlayerMapFileName = "bedrock-players.json";
    private const string PlayerDataFileName = "player-data.json";
    private const string BanFileName = "bedrock-bans.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _appDataDirectory;
    private readonly string _serverRoot;
    private readonly ILogger<BedrockPlayerDataService>? _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public BedrockPlayerDataService(
        string appDataDirectory,
        string serverRoot,
        ILogger<BedrockPlayerDataService>? logger = null)
    {
        _appDataDirectory = appDataDirectory;
        _serverRoot = serverRoot;
        _logger = logger;
    }

    public async Task UpsertPlayerAsync(string xuid, string name)
    {
        if (string.IsNullOrWhiteSpace(xuid) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _fileLock.WaitAsync();
        try
        {
            Dictionary<string, string> map = await ReadPlayerMapAsync();
            map[xuid.Trim()] = name.Trim();
            await WriteJsonAtomicallyAsync(GetPlayerMapPath(), map);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<int> ImportPlayerMapFromLogLinesAsync(IEnumerable<string> lines)
    {
        Dictionary<string, string> parsed = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            Match match = ConnectionRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            string name = match.Groups["name"].Value.Trim();
            string xuid = match.Groups["xuid"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(xuid))
            {
                parsed[xuid] = name;
            }
        }

        if (parsed.Count == 0)
        {
            return 0;
        }

        await _fileLock.WaitAsync();
        try
        {
            Dictionary<string, string> map = await ReadPlayerMapAsync();
            foreach (KeyValuePair<string, string> player in parsed)
            {
                map[player.Key] = player.Value;
            }

            await WriteJsonAtomicallyAsync(GetPlayerMapPath(), map);
        }
        finally
        {
            _fileLock.Release();
        }

        return parsed.Count;
    }

    public async Task<string?> GetXuidAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        Dictionary<string, string> map = await ReadPlayerMapAsync();
        return map.FirstOrDefault(pair =>
            string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
    }

    public async Task<string?> GetNameAsync(string xuid)
    {
        if (string.IsNullOrWhiteSpace(xuid))
        {
            return null;
        }

        Dictionary<string, string> map = await ReadPlayerMapAsync();
        return map.TryGetValue(xuid.Trim(), out string? name) ? name : null;
    }

    public async Task<HashSet<string>> GetOppedPlayerNamesAsync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> playerMap = await ReadPlayerMapAsync();

        try
        {
            JsonDocument? document = await ReadJsonDocumentWithRetriesAsync(Path.Combine(_serverRoot, "permissions.json"));
            if (document == null)
            {
                return names;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return names;
                }

                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    string permission = TryGetString(element, "permission");
                    if (!string.Equals(permission, "operator", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string xuid = TryGetString(element, "xuid");
                    if (!string.IsNullOrWhiteSpace(xuid) &&
                        playerMap.TryGetValue(xuid, out string? name) &&
                        !string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read Bedrock permissions.json from {ServerRoot}.", _serverRoot);
        }

        return names;
    }

    public async Task<HashSet<string>> GetOperatorXuidsAsync()
    {
        var xuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            JsonDocument? document = await ReadJsonDocumentWithRetriesAsync(Path.Combine(_serverRoot, "permissions.json"));
            if (document == null)
            {
                return xuids;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return xuids;
                }

                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    string permission = TryGetString(element, "permission");
                    string xuid = TryGetString(element, "xuid");
                    if (string.Equals(permission, "operator", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(xuid))
                    {
                        xuids.Add(xuid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read Bedrock operator XUIDs from permissions.json in {ServerRoot}.", _serverRoot);
        }

        return xuids;
    }

    public IDisposable WatchPermissionsFile(Action onChanged)
    {
        if (string.IsNullOrWhiteSpace(_serverRoot) || !Directory.Exists(_serverRoot))
        {
            return EmptyDisposable.Instance;
        }

        var debouncer = new DebouncedFileChange(
            onChanged,
            ex => _logger?.LogWarning(ex, "Failed to handle Bedrock permissions.json change for {ServerRoot}.", _serverRoot));

        var watcher = new FileSystemWatcher(_serverRoot, "permissions.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime |
                           NotifyFilters.Size
        };

        FileSystemEventHandler handler = (_, _) => debouncer.Signal();
        RenamedEventHandler renamedHandler = (_, _) => debouncer.Signal();
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += renamedHandler;
        watcher.EnableRaisingEvents = true;

        return new DelegateDisposable(() =>
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= handler;
            watcher.Created -= handler;
            watcher.Deleted -= handler;
            watcher.Renamed -= renamedHandler;
            watcher.Dispose();
            debouncer.Dispose();
        });
    }

    public async Task<string> GetGamemodeAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "survival";
        }

        PlayerDataSidecar sidecar = await ReadPlayerDataSidecarAsync();
        KeyValuePair<string, BedrockPlayerRecord> match = sidecar.Players.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(match.Key))
        {
            await SaveGamemodeAsync(name, "survival");
            return "survival";
        }

        return NormalizeGamemode(match.Value.Gamemode);
    }

    public async Task SaveGamemodeAsync(string name, string gamemode)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _fileLock.WaitAsync();
        try
        {
            PlayerDataSidecar sidecar = await ReadPlayerDataSidecarAsync();
            string key = sidecar.Players.Keys.FirstOrDefault(existing =>
                string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) ?? name.Trim();
            sidecar.Players[key] = new BedrockPlayerRecord
            {
                Gamemode = NormalizeGamemode(gamemode),
                LastSeen = DateTime.UtcNow
            };

            await WriteJsonAtomicallyAsync(GetPlayerDataPath(), sidecar);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<BedrockBanEntry>> GetBansAsync()
    {
        try
        {
            List<BedrockBanEntry>? bans = await ReadJsonAsync<List<BedrockBanEntry>>(GetBanPath());
            return bans ?? new List<BedrockBanEntry>();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read Bedrock ban sidecar from {Path}.", GetBanPath());
            return new List<BedrockBanEntry>();
        }
    }

    public async Task<BedrockBanEntry?> GetBanForPlayerAsync(string name, string? xuid)
    {
        List<BedrockBanEntry> bans = await GetBansAsync();
        return bans.FirstOrDefault(ban =>
            (!string.IsNullOrWhiteSpace(xuid) &&
             string.Equals(ban.Xuid, xuid, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(ban.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddBanAsync(BedrockBanEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            return;
        }

        await _fileLock.WaitAsync();
        try
        {
            List<BedrockBanEntry> bans = await GetBansAsync();
            bans.RemoveAll(ban => string.Equals(ban.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            bans.Add(entry);
            await WriteJsonAtomicallyAsync(GetBanPath(), bans);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RemoveBanAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _fileLock.WaitAsync();
        try
        {
            List<BedrockBanEntry> bans = await GetBansAsync();
            int removed = bans.RemoveAll(ban => string.Equals(ban.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await WriteJsonAtomicallyAsync(GetBanPath(), bans);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadPlayerMapAsync()
    {
        Dictionary<string, string>? map = await ReadJsonAsync<Dictionary<string, string>>(GetPlayerMapPath());
        return map == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<PlayerDataSidecar> ReadPlayerDataSidecarAsync()
    {
        PlayerDataSidecar? sidecar = await ReadJsonAsync<PlayerDataSidecar>(GetPlayerDataPath());
        return sidecar ?? new PlayerDataSidecar();
    }

    private string GetPlayerMapPath() => Path.Combine(_appDataDirectory, PlayerMapFileName);

    private string GetPlayerDataPath() => Path.Combine(_appDataDirectory, PlayerDataFileName);

    private string GetBanPath() => Path.Combine(_appDataDirectory, BanFileName);

    private static string NormalizeGamemode(string? gamemode)
    {
        if (string.IsNullOrWhiteSpace(gamemode))
        {
            return "survival";
        }

        string normalized = gamemode.Trim().ToLowerInvariant();
        return normalized is "creative" or "adventure" or "spectator" or "survival"
            ? normalized
            : "survival";
    }

    private async Task<T?> ReadJsonAsync<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read JSON file {Path}.", path);
            return default;
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static async Task<JsonDocument?> ReadJsonDocumentWithRetriesAsync(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string? json = await ReadTextWithRetriesAsync(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException) when (attempt < 2)
            {
                await Task.Delay(150);
            }
        }

        string? finalJson = await ReadTextWithRetriesAsync(path);
        return string.IsNullOrWhiteSpace(finalJson)
            ? null
            : JsonDocument.Parse(finalJson);
    }

    private static async Task<string?> ReadTextWithRetriesAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(150);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                await Task.Delay(150);
            }
        }

        using var finalStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var finalReader = new StreamReader(finalStream);
        return await finalReader.ReadToEndAsync();
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed class PlayerDataSidecar
    {
        public Dictionary<string, BedrockPlayerRecord> Players { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class BedrockPlayerRecord
    {
        public string Gamemode { get; set; } = "survival";
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    private sealed class DebouncedFileChange : IDisposable
    {
        private readonly Action _onChanged;
        private readonly Action<Exception> _onError;
        private readonly Timer _timer;

        public DebouncedFileChange(Action onChanged, Action<Exception> onError)
        {
            _onChanged = onChanged;
            _onError = onError;
            _timer = new Timer(OnElapsed);
        }

        public void Signal() => _timer.Change(500, Timeout.Infinite);

        private void OnElapsed(object? state)
        {
            try
            {
                _onChanged();
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }

        public void Dispose() => _timer.Dispose();
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public DelegateDisposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _dispose();
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
