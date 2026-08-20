using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using fNbt;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Players;

public sealed class PlayerDataService
{
    private readonly string _serverRoot;
    private readonly ILogger<PlayerDataService>? _logger;

    public PlayerDataService(string serverRoot, ILogger<PlayerDataService>? logger = null)
    {
        _serverRoot = serverRoot;
        _logger = logger;
    }

    public async Task<string?> GetUuidAsync(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        // 1. Try usercache.json
        string? uuid = await TryGetUuidFromJsonArrayFileAsync("usercache.json", playerName);
        if (uuid != null)
        {
            return uuid;
        }

        // 2. Try ops.json
        uuid = await TryGetUuidFromJsonArrayFileAsync("ops.json", playerName);
        if (uuid != null)
        {
            return uuid;
        }

        // 3. Try whitelist.json
        uuid = await TryGetUuidFromJsonArrayFileAsync("whitelist.json", playerName);
        if (uuid != null)
        {
            return uuid;
        }

        // 4. Try banned-players.json
        uuid = await TryGetUuidFromJsonArrayFileAsync("banned-players.json", playerName);
        if (uuid != null)
        {
            return uuid;
        }

        // 5. Try offline player UUID computation
        string offlineUuid = ComputeOfflinePlayerUuid(playerName);
        if (!string.IsNullOrWhiteSpace(offlineUuid))
        {
            string path = GetPlayerDataFilePath(offlineUuid);
            if (File.Exists(path))
            {
                return offlineUuid;
            }
        }

        return !string.IsNullOrWhiteSpace(offlineUuid) ? offlineUuid : null;
    }

    public static string ComputeOfflinePlayerUuid(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return string.Empty;
        }

        byte[] input = System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName);
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(input);

        // Version 3 (MD5-based UUID)
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        // IETF variant
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return string.Format(
            "{0:x2}{1:x2}{2:x2}{3:x2}-{4:x2}{5:x2}-{6:x2}{7:x2}-{8:x2}{9:x2}-{10:x2}{11:x2}{12:x2}{13:x2}{14:x2}{15:x2}",
            hash[0], hash[1], hash[2], hash[3],
            hash[4], hash[5],
            hash[6], hash[7],
            hash[8], hash[9],
            hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]);
    }

    private async Task<string?> TryGetUuidFromJsonArrayFileAsync(string fileName, string playerName)
    {
        try
        {
            string path = Path.Combine(_serverRoot, fileName);
            JsonDocument? document = await ReadJsonDocumentWithRetriesAsync(path);
            if (document == null)
            {
                return null;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    string name = TryGetString(element, "name");
                    if (!string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string uuid = TryGetString(element, "uuid");
                    return IsSafeUuidFileName(uuid) ? uuid : null;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to read UUID for {PlayerName} from {FileName} in {ServerRoot}.", playerName, fileName, _serverRoot);
        }

        return null;
    }

    public async Task<string> GetGamemodeAsync(string uuid)
    {
        if (!IsSafeUuidFileName(uuid))
        {
            return "survival";
        }

        string path = GetPlayerDataFilePath(uuid);
        if (!File.Exists(path))
        {
            return "survival";
        }

        try
        {
            var nbtFile = new NbtFile();
            await Task.Run(() => nbtFile.LoadFromFile(path));
            var root = nbtFile.RootTag;
            var gamemodeTag = root?["playerGameType"] as NbtInt;

            return gamemodeTag?.Value switch
            {
                1 => "creative",
                2 => "adventure",
                3 => "spectator",
                _ => "survival"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read Java playerdata file {Path}.", path);
            return "survival";
        }
    }

    public async Task<HashSet<string>> GetOppedPlayersAsync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            JsonDocument? document = await ReadJsonDocumentWithRetriesAsync(Path.Combine(_serverRoot, "ops.json"));
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
                    string name = TryGetString(element, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read Java ops.json from {ServerRoot}.", _serverRoot);
        }

        return names;
    }

    public IDisposable WatchForChanges(Action onOpsChanged, Action<string> onPlayerdataChanged)
    {
        return WatchForChanges(_ => onOpsChanged(), onPlayerdataChanged);
    }

    public IDisposable WatchForChanges(Action<string> onOpsChanged, Action<string> onPlayerdataChanged)
    {
        if (string.IsNullOrWhiteSpace(_serverRoot) || !Directory.Exists(_serverRoot))
        {
            return EmptyDisposable.Instance;
        }

        var disposables = new List<IDisposable>();
        string opsPath = Path.Combine(_serverRoot, "ops.json");
        var opsDebouncer = new DebouncedFileChange(
            () => onOpsChanged(opsPath),
            ex => _logger?.LogWarning(ex, "Failed to handle Java ops.json change for {ServerRoot}.", _serverRoot));
        disposables.Add(opsDebouncer);

        var opsWatcher = new FileSystemWatcher(_serverRoot, "ops.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime |
                           NotifyFilters.Size
        };

        FileSystemEventHandler opsHandler = (_, _) => opsDebouncer.Signal();
        RenamedEventHandler opsRenamedHandler = (_, _) => opsDebouncer.Signal();
        opsWatcher.Changed += opsHandler;
        opsWatcher.Created += opsHandler;
        opsWatcher.Deleted += opsHandler;
        opsWatcher.Renamed += opsRenamedHandler;
        opsWatcher.EnableRaisingEvents = true;
        disposables.Add(new DelegateDisposable(() =>
        {
            opsWatcher.EnableRaisingEvents = false;
            opsWatcher.Changed -= opsHandler;
            opsWatcher.Created -= opsHandler;
            opsWatcher.Deleted -= opsHandler;
            opsWatcher.Renamed -= opsRenamedHandler;
            opsWatcher.Dispose();
        }));

        string playerDataDirectory = GetPlayerDataDirectory();
        if (!Directory.Exists(playerDataDirectory))
        {
            // Fallback check: if default "world/playerdata" exists
            string defaultDir = Path.Combine(_serverRoot, "world", "playerdata");
            if (Directory.Exists(defaultDir))
            {
                playerDataDirectory = defaultDir;
            }
        }

        if (Directory.Exists(playerDataDirectory))
        {
            var playerdataWatcher = new FileSystemWatcher(playerDataDirectory, "*.dat")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.CreationTime |
                               NotifyFilters.Size
            };

            FileSystemEventHandler playerdataHandler = (_, e) => NotifyPlayerdataChanged(e.FullPath, onPlayerdataChanged);
            RenamedEventHandler playerdataRenamedHandler = (_, e) => NotifyPlayerdataChanged(e.FullPath, onPlayerdataChanged);
            playerdataWatcher.Changed += playerdataHandler;
            playerdataWatcher.Created += playerdataHandler;
            playerdataWatcher.Renamed += playerdataRenamedHandler;
            playerdataWatcher.EnableRaisingEvents = true;
            disposables.Add(new DelegateDisposable(() =>
            {
                playerdataWatcher.EnableRaisingEvents = false;
                playerdataWatcher.Changed -= playerdataHandler;
                playerdataWatcher.Created -= playerdataHandler;
                playerdataWatcher.Renamed -= playerdataRenamedHandler;
                playerdataWatcher.Dispose();
            }));
        }

        return new CompositeDisposable(disposables);
    }

    public string GetPlayerDataDirectory()
    {
        string levelName = GetLevelName();
        string candidate = Path.Combine(_serverRoot, levelName, "playerdata");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        string worldCandidate = Path.Combine(_serverRoot, "world", "playerdata");
        if (Directory.Exists(worldCandidate))
        {
            return worldCandidate;
        }

        // Try searching any immediate subdirectories for a "playerdata" directory
        try
        {
            if (Directory.Exists(_serverRoot))
            {
                foreach (string dir in Directory.GetDirectories(_serverRoot))
                {
                    string nested = Path.Combine(dir, "playerdata");
                    if (Directory.Exists(nested))
                    {
                        return nested;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to inspect server directories for playerdata folder in {ServerRoot}.", _serverRoot);
        }

        return candidate;
    }

    public string GetPlayerDataFilePath(string uuid)
    {
        string dir = GetPlayerDataDirectory();
        string primaryPath = Path.Combine(dir, $"{uuid}.dat");
        if (File.Exists(primaryPath))
        {
            return primaryPath;
        }

        string fallbackPath = Path.Combine(_serverRoot, "world", "playerdata", $"{uuid}.dat");
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        return primaryPath;
    }

    public string GetLevelName()
    {
        string propsPath = Path.Combine(_serverRoot, "server.properties");
        if (!File.Exists(propsPath))
        {
            return "world";
        }

        try
        {
            foreach (string line in File.ReadAllLines(propsPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("!", StringComparison.Ordinal))
                {
                    continue;
                }

                int eqIndex = trimmed.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = trimmed[..eqIndex].Trim();
                    if (string.Equals(key, "level-name", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed[(eqIndex + 1)..].Trim();
                        return string.IsNullOrWhiteSpace(value) ? "world" : value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read level-name from server.properties in {ServerRoot}.", _serverRoot);
        }

        return "world";
    }

    private static void NotifyPlayerdataChanged(string path, Action<string> onPlayerdataChanged)
    {
        string uuid = Path.GetFileNameWithoutExtension(path);
        if (IsSafeUuidFileName(uuid))
        {
            onPlayerdataChanged(uuid);
        }
    }

    private static bool IsSafeUuidFileName(string? uuid)
    {
        return !string.IsNullOrWhiteSpace(uuid) &&
               Guid.TryParse(uuid, out _) &&
               string.Equals(Path.GetFileName(uuid), uuid, StringComparison.Ordinal);
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

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
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

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _disposables;
        private int _disposed;

        public CompositeDisposable(IReadOnlyList<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (IDisposable disposable in _disposables.Reverse())
            {
                disposable.Dispose();
            }
        }
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
