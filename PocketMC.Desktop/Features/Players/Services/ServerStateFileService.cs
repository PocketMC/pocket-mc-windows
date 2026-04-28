using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Helpers;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Players.Services;

public sealed class BannedPlayerEntry
{
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
    public string Expires { get; set; } = "forever";
    public bool IsSidecar { get; set; }
}

public sealed class ServerStateFileService
{
    private readonly InstanceRegistry _registry;
    private readonly ILogger<ServerStateFileService> _logger;

    public ServerStateFileService(
        InstanceRegistry registry,
        ILogger<ServerStateFileService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<List<string>> GetOppedPlayersAsync(InstanceMetadata instance)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot))
        {
            return new List<string>();
        }

        try
        {
            if (CommandFormatter.IsBedrock(instance.ServerType))
            {
                return await ReadBedrockOperatorsAsync(Path.Combine(serverRoot, "permissions.json"));
            }

            if (CommandFormatter.IsPocketMine(instance.ServerType))
            {
                return await ReadPlainNameFileAsync(Path.Combine(serverRoot, "ops.txt"));
            }

            return await ReadJsonNamesAsync(Path.Combine(serverRoot, "ops.json"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read operator state for instance {InstanceId}.", instance.Id);
            return new List<string>();
        }
    }

    public async Task<List<BannedPlayerEntry>> GetBannedPlayersAsync(InstanceMetadata instance)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot))
        {
            return new List<BannedPlayerEntry>();
        }

        try
        {
            if (CommandFormatter.IsBedrock(instance.ServerType))
            {
                return new List<BannedPlayerEntry>();
            }

            if (CommandFormatter.IsPocketMine(instance.ServerType))
            {
                List<string> names = await ReadPlainNameFileAsync(Path.Combine(serverRoot, "banned.txt"));
                return names.Select(name => new BannedPlayerEntry
                {
                    Name = name,
                    Reason = string.Empty,
                    Created = string.Empty,
                    Expires = "forever"
                }).ToList();
            }

            return await ReadJavaBansAsync(Path.Combine(serverRoot, "banned-players.json"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read ban state for instance {InstanceId}.", instance.Id);
            return new List<BannedPlayerEntry>();
        }
    }

    public IDisposable WatchForChanges(InstanceMetadata instance, Action onChanged)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot) || !Directory.Exists(serverRoot))
        {
            return EmptyDisposable.Instance;
        }

        HashSet<string> watchedFiles = GetWatchedFileNames(instance)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (watchedFiles.Count == 0)
        {
            return EmptyDisposable.Instance;
        }

        var debouncer = new DebouncedFileChange(onChanged, ex =>
            _logger.LogWarning(ex, "Failed to handle state file change for instance {InstanceId}.", instance.Id));

        var watcher = new FileSystemWatcher(serverRoot)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime |
                           NotifyFilters.Size
        };

        FileSystemEventHandler fileHandler = (_, e) =>
        {
            if (watchedFiles.Contains(Path.GetFileName(e.FullPath)))
            {
                debouncer.Signal();
            }
        };

        RenamedEventHandler renameHandler = (_, e) =>
        {
            if (watchedFiles.Contains(Path.GetFileName(e.FullPath)) ||
                watchedFiles.Contains(Path.GetFileName(e.OldFullPath)))
            {
                debouncer.Signal();
            }
        };

        watcher.Changed += fileHandler;
        watcher.Created += fileHandler;
        watcher.Deleted += fileHandler;
        watcher.Renamed += renameHandler;
        watcher.EnableRaisingEvents = true;

        return new DelegateDisposable(() =>
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= fileHandler;
            watcher.Created -= fileHandler;
            watcher.Deleted -= fileHandler;
            watcher.Renamed -= renameHandler;
            watcher.Dispose();
            debouncer.Dispose();
        });
    }

    private static IEnumerable<string> GetWatchedFileNames(InstanceMetadata instance)
    {
        if (CommandFormatter.IsBedrock(instance.ServerType))
        {
            yield return "permissions.json";
            yield return BanSidecarService.FileName;
            yield break;
        }

        if (CommandFormatter.IsPocketMine(instance.ServerType))
        {
            yield return "ops.txt";
            yield return "banned.txt";
            yield return "banned-ips.txt";
            yield break;
        }

        yield return "ops.json";
        yield return "banned-players.json";
        yield return "banned-ips.json";
        yield return "whitelist.json";
    }

    private static async Task<List<string>> ReadJsonNamesAsync(string path)
    {
        string? json = await ReadTextWithRetriesAsync(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        List<string> names = new();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("name", out JsonElement nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                string? name = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static async Task<List<string>> ReadBedrockOperatorsAsync(string path)
    {
        string? json = await ReadTextWithRetriesAsync(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        List<string> names = new();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            string permission = TryGetString(element, "permission");
            if (!string.Equals(permission, "operator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = TryGetString(element, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static async Task<List<BannedPlayerEntry>> ReadJavaBansAsync(string path)
    {
        string? json = await ReadTextWithRetriesAsync(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<BannedPlayerEntry>();
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new List<BannedPlayerEntry>();
        }

        List<BannedPlayerEntry> entries = new();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            string name = TryGetString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            entries.Add(new BannedPlayerEntry
            {
                Name = name,
                Reason = TryGetString(element, "reason"),
                Created = TryGetString(element, "created"),
                Expires = string.IsNullOrWhiteSpace(TryGetString(element, "expires"))
                    ? "forever"
                    : TryGetString(element, "expires")
            });
        }

        return entries;
    }

    private static async Task<List<string>> ReadPlainNameFileAsync(string path)
    {
        string? text = await ReadTextWithRetriesAsync(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();
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
