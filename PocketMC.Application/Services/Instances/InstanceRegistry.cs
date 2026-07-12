using PocketMC.Domain.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PocketMC.Application.Services.Instances;

/// <summary>
/// Maintains an in-memory registry of all discovered Minecraft instances.
/// Handles metadata caching and broadcasts changes to the ecosystem.
/// </summary>
public sealed class InstanceRegistry
{
    private readonly InstancePathService _pathService;
    private readonly ILogger<InstanceRegistry> _logger;

    private readonly ConcurrentDictionary<Guid, string> _pathCache = new();
    private readonly ConcurrentDictionary<Guid, InstanceMetadata> _metadataCache = new();
    private readonly object _cacheLock = new();
    private volatile bool _cacheInitialized;

    public event EventHandler? InstancesChanged;

    public InstanceRegistry(InstancePathService pathService, ILogger<InstanceRegistry> logger)
    {
        _pathService = pathService;
        _logger = logger;
    }

    public IReadOnlyList<InstanceMetadata> GetAll()
    {
        EnsureCacheLoaded();
        return _metadataCache.Values.ToList();
    }

    public InstanceMetadata? GetById(Guid id)
    {
        EnsureCacheLoaded();
        return _metadataCache.TryGetValue(id, out var metadata) ? metadata : null;
    }

    public string? GetPath(Guid id)
    {
        EnsureCacheLoaded();
        return _pathCache.TryGetValue(id, out var path) ? path : null;
    }

    public void Register(InstanceMetadata metadata, string path)
    {
        _pathCache[metadata.Id] = path;
        _metadataCache[metadata.Id] = metadata;
        _cacheInitialized = true;
        NotifyChanged();
    }

    public void Unregister(Guid id)
    {
        _pathCache.TryRemove(id, out _);
        _metadataCache.TryRemove(id, out _);
        NotifyChanged();
    }

    public void Refresh()
    {
        lock (_cacheLock)
        {
            RefreshFromDisk();
            _cacheInitialized = true;
        }
        NotifyChanged();
    }

    private void EnsureCacheLoaded()
    {
        if (_cacheInitialized) return;

        lock (_cacheLock)
        {
            if (_cacheInitialized) return;
            RefreshFromDisk();
            _cacheInitialized = true;
        }
    }

    private void RefreshFromDisk()
    {
        _logger.LogInformation("Refreshing instance registry from disk...");
        _pathCache.Clear();
        _metadataCache.Clear();

        string root = _pathService.GetServersRoot();
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.GetDirectories(root))
        {
            string metadataFile = _pathService.GetMetadataPath(dir);
            if (TryReadMetadata(metadataFile, out var metadata) && metadata != null)
            {
                _pathCache[metadata.Id] = dir;
                _metadataCache[metadata.Id] = metadata;
            }
        }
    }

    private bool TryReadMetadata(string file, out InstanceMetadata? metadata)
    {
        metadata = null;
        if (!File.Exists(file)) return false;

        try
        {
            string content = File.ReadAllText(file);
            metadata = JsonSerializer.Deserialize<InstanceMetadata>(content);
            return metadata != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipping malformed metadata at {File}", file);
            return false;
        }
    }

    private void NotifyChanged()
    {
        try
        {
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while notifying instance registry change.");
        }
    }
}

