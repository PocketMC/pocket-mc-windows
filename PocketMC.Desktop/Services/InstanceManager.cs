using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Services
{
    public class InstanceManager
    {
        private readonly ApplicationState _applicationState;
        private readonly ILogger<InstanceManager> _logger;
        private readonly ConcurrentDictionary<Guid, string> _pathCache = new();
        private readonly ConcurrentDictionary<Guid, InstanceMetadata> _metadataCache = new();
        private readonly object _cacheLock = new();
        private volatile bool _cacheInitialized;

        public InstanceManager(ApplicationState applicationState, ILogger<InstanceManager> logger)
        {
            _applicationState = applicationState;
            _logger = logger;
        }

        private string ServersDirectory => _applicationState.GetServersDirectory();

        private void EnsureDirectory()
        {
            if (!_applicationState.IsConfigured)
            {
                throw new InvalidOperationException("PocketMC is not configured with an app root path yet.");
            }

            if (!Directory.Exists(ServersDirectory))
            {
                Directory.CreateDirectory(ServersDirectory);
            }
        }

        public List<InstanceMetadata> GetAllInstances()
        {
            EnsureDirectory();
            EnsureCacheLoaded();
            return _metadataCache.Values.ToList();
        }

        public string? GetInstancePath(Guid id)
        {
            EnsureDirectory();

            if (_pathCache.TryGetValue(id, out var cachedPath) && Directory.Exists(cachedPath))
            {
                return cachedPath;
            }

            EnsureCacheLoaded();
            if (_pathCache.TryGetValue(id, out cachedPath) && Directory.Exists(cachedPath))
            {
                return cachedPath;
            }

            foreach (var dir in Directory.GetDirectories(ServersDirectory))
            {
                var metadataFile = Path.Combine(dir, ".pocket-mc.json");
                if (TryReadMetadata(metadataFile, out var metadata) && metadata != null)
                {
                    _pathCache[metadata.Id] = dir;
                    _metadataCache[metadata.Id] = metadata;
                    if (metadata.Id == id)
                    {
                        return dir;
                    }
                }
            }

            return null;
        }

        public InstanceMetadata CreateInstance(string name, string description, string serverType = "Vanilla", string minecraftVersion = "1.20.4")
        {
            EnsureDirectory();

            string baseSlug = SlugHelper.GenerateSlug(name);
            string slug = baseSlug;
            int counter = 2;

            while (Directory.Exists(Path.Combine(ServersDirectory, slug)))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            var newInstancePath = Path.Combine(ServersDirectory, slug);
            Directory.CreateDirectory(newInstancePath);

            var metadata = new InstanceMetadata
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                ServerType = serverType,
                MinecraftVersion = minecraftVersion,
                CreatedAt = DateTime.UtcNow
            };

            var metadataFile = Path.Combine(newInstancePath, ".pocket-mc.json");
            File.WriteAllText(metadataFile, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

            _pathCache[metadata.Id] = newInstancePath;
            _metadataCache[metadata.Id] = metadata;
            _cacheInitialized = true;

            return metadata;
        }

        public void UpdateMetadata(string folderName, string newName, string newDescription)
        {
            var oldFolderPath = Path.Combine(ServersDirectory, folderName);
            if (!Directory.Exists(oldFolderPath)) return;

            // 1. Calculate new slug
            string baseSlug = SlugHelper.GenerateSlug(newName);
            string newSlug = baseSlug;
            int counter = 2;
            
            while (Directory.Exists(Path.Combine(ServersDirectory, newSlug)) && newSlug != folderName)
            {
                newSlug = $"{baseSlug}-{counter}";
                counter++;
            }

            var currentFolderPath = oldFolderPath;

            // 2. Safely rename folder first (avoids antivirus file lock race conditions on the directory)
            if (newSlug != folderName)
            {
                var newFolderPath = Path.Combine(ServersDirectory, newSlug);
                try
                {
                    Directory.Move(oldFolderPath, newFolderPath);
                    currentFolderPath = newFolderPath;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to rename instance folder {OldFolderPath} to {NewFolderPath}.", oldFolderPath, newFolderPath);
                    // Fallback to original path if locked
                    currentFolderPath = oldFolderPath;
                }
            }

            // 3. Write updated JSON inside the definitive folder
            var metadataFile = Path.Combine(currentFolderPath, ".pocket-mc.json");
            if (File.Exists(metadataFile))
            {
                var content = File.ReadAllText(metadataFile);
                var metadata = JsonSerializer.Deserialize<InstanceMetadata>(content) ?? new InstanceMetadata();
                metadata.Name = newName;
                metadata.Description = newDescription;

                File.WriteAllText(metadataFile, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
                _pathCache[metadata.Id] = currentFolderPath;
                _metadataCache[metadata.Id] = metadata;
            }
        }

        public void DeleteInstance(string folderName)
        {
            var folderPath = Path.Combine(ServersDirectory, folderName);
            Guid? instanceId = ReadInstanceId(folderPath);
            bool deleted = false;
            if (Directory.Exists(folderPath))
            {
                // Simple retry logic since files might be temporarily locked
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Directory.Delete(folderPath, true);
                        deleted = true;
                        break;
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Delete attempt {Attempt} failed for instance folder {FolderPath}.", i + 1, folderPath);
                        Thread.Sleep(500); // Wait 500ms and retry
                    }
                }
            }

            if (deleted && instanceId.HasValue)
            {
                _pathCache.TryRemove(instanceId.Value, out _);
                _metadataCache.TryRemove(instanceId.Value, out _);
            }
        }

        public void OpenInExplorer(string folderName)
        {
            var folderPath = Path.Combine(ServersDirectory, folderName);
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
        }

        public void AcceptEula(string folderName)
        {
            var folderPath = Path.Combine(ServersDirectory, folderName);
            if (Directory.Exists(folderPath))
            {
                File.WriteAllText(Path.Combine(folderPath, "eula.txt"), 
                    "# By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n" +
                    "eula=true\n");
            }
        }

        public void SaveMetadata(InstanceMetadata metadata, string instancePath)
        {
            var metadataFile = Path.Combine(instancePath, ".pocket-mc.json");
            File.WriteAllText(metadataFile, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            _pathCache[metadata.Id] = instancePath;
            _metadataCache[metadata.Id] = metadata;
            _cacheInitialized = true;
        }

        private void EnsureCacheLoaded()
        {
            if (_cacheInitialized)
            {
                return;
            }

            lock (_cacheLock)
            {
                if (_cacheInitialized)
                {
                    return;
                }

                RefreshCachesFromDisk();
                _cacheInitialized = true;
            }
        }

        private void RefreshCachesFromDisk()
        {
            _pathCache.Clear();
            _metadataCache.Clear();

            foreach (var dir in Directory.GetDirectories(ServersDirectory))
            {
                var metadataFile = Path.Combine(dir, ".pocket-mc.json");
                if (TryReadMetadata(metadataFile, out var metadata) && metadata != null)
                {
                    _pathCache[metadata.Id] = dir;
                    _metadataCache[metadata.Id] = metadata;
                }
            }
        }

        private bool TryReadMetadata(string metadataFile, out InstanceMetadata? metadata)
        {
            metadata = null;
            if (!File.Exists(metadataFile))
            {
                return false;
            }

            try
            {
                var content = File.ReadAllText(metadataFile);
                metadata = JsonSerializer.Deserialize<InstanceMetadata>(content);
                return metadata != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping malformed instance metadata file {MetadataFile}.", metadataFile);
                return false;
            }
        }

        private Guid? ReadInstanceId(string folderPath)
        {
            var metadataFile = Path.Combine(folderPath, ".pocket-mc.json");
            return TryReadMetadata(metadataFile, out var metadata) ? metadata?.Id : null;
        }
    }
}
