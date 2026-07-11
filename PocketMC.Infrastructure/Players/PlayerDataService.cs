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

        try
        {
            JsonDocument? document = await ReadJsonDocumentWithRetriesAsync(Path.Combine(_serverRoot, "usercache.json"));
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
            _logger?.LogWarning(ex, "Failed to read Java usercache.json from {ServerRoot}.", _serverRoot);
        }

        return null;
    }

    public async Task<string> GetGamemodeAsync(string uuid)
    {
        if (!IsSafeUuidFileName(uuid))
        {
            return "survival";
        }

        string path = Path.Combine(_serverRoot, "world", "playerdata", $"{uuid}.dat");
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

        string playerDataDirectory = Path.Combine(_serverRoot, "world", "playerdata");
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
