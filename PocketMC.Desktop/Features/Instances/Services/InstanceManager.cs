using PocketMC.Desktop.Features.Instances.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Features.Instances.Services;
    /// <summary>
    /// Orchestrates the lifecycle of Minecraft server instances, including
    /// creation, deletion, configuration updates, and filesystem interactions.
    /// </summary>
    public sealed class InstanceManager
    {
        private readonly InstanceRegistry _registry;
        private readonly InstancePathService _pathService;
        private readonly ApplicationState _applicationState;
        private readonly IAssetProvider _assetProvider;
        private readonly ILogger<InstanceManager> _logger;

        private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

        public InstanceManager(
            InstanceRegistry registry,
            InstancePathService pathService,
            ApplicationState applicationState,
            IAssetProvider assetProvider,
            ILogger<InstanceManager> logger)
        {
            _registry = registry;
            _pathService = pathService;
            _applicationState = applicationState;
            _assetProvider = assetProvider;
            _logger = logger;
        }

        public InstanceMetadata CreateInstance(string name, string description, string serverType = "Vanilla", string minecraftVersion = "1.20.4")
        {
            _pathService.EnsureServersRootExists();

            string baseSlug = SlugHelper.GenerateSlug(name);
            string slug = baseSlug;
            int counter = 2;

            while (Directory.Exists(_pathService.GetInstancePath(slug)))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            string newInstancePath = _pathService.GetInstancePath(slug);
            Directory.CreateDirectory(newInstancePath);

            // Apply default server icon
            ApplyDefaultServerIcon(newInstancePath);

            var metadata = new InstanceMetadata
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                ServerType = serverType,
                MinecraftVersion = minecraftVersion,
                CreatedAt = DateTime.UtcNow
            };

            SaveMetadata(metadata, newInstancePath);
            return metadata;
        }

        private void ApplyDefaultServerIcon(string instancePath)
        {
            try
            {
                using var stream = _assetProvider.GetAssetStream("logo.png");
                if (stream != null)
                {
                    using var fileStream = File.Create(Path.Combine(instancePath, "server-icon.png"));
                    stream.CopyTo(fileStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply default server icon to new instance at {InstancePath}.", instancePath);
            }
        }

        public void UpdateMetadata(string folderName, string newName, string newDescription)
        {
            string oldFolderPath = _pathService.GetInstancePath(folderName);
            if (!Directory.Exists(oldFolderPath)) return;

            string baseSlug = SlugHelper.GenerateSlug(newName);
            string newSlug = baseSlug;
            int counter = 2;

            while (Directory.Exists(_pathService.GetInstancePath(newSlug)) && newSlug != folderName)
            {
                newSlug = $"{baseSlug}-{counter}";
                counter++;
            }

            string currentFolderPath = oldFolderPath;

            if (newSlug != folderName)
            {
                string newFolderPath = _pathService.GetInstancePath(newSlug);
                try
                {
                    Directory.Move(oldFolderPath, newFolderPath);
                    currentFolderPath = newFolderPath;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to rename instance folder {OldFolderPath} to {NewFolderPath}.", oldFolderPath, newFolderPath);
                    currentFolderPath = oldFolderPath;
                }
            }

            string metadataFile = _pathService.GetMetadataPath(currentFolderPath);
            if (File.Exists(metadataFile))
            {
                string content = File.ReadAllText(metadataFile);
                var metadata = JsonSerializer.Deserialize<InstanceMetadata>(content) ?? new InstanceMetadata();
                metadata.Name = newName;
                metadata.Description = newDescription;

                SaveMetadata(metadata, currentFolderPath);
            }
        }

        public async Task<bool> DeleteInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            string? folderPath = _registry.GetPath(instanceId);
            if (folderPath == null) return false;

            bool deleted = await DeleteDirectoryWithRetryAsync(folderPath, cancellationToken);
            if (deleted)
            {
                _registry.Unregister(instanceId);
            }

            return deleted;
        }

        public async Task<bool> DeleteInstanceAsync(string folderName, CancellationToken cancellationToken = default)
        {
            string folderPath = _pathService.GetInstancePath(folderName);
            return await DeleteDirectoryWithRetryAsync(folderPath, cancellationToken);
        }

        public void SaveMetadata(InstanceMetadata metadata, string instancePath)
        {
            string metadataFile = _pathService.GetMetadataPath(instancePath);
            FileUtils.AtomicWriteAllText(metadataFile, JsonSerializer.Serialize(metadata, MetadataJsonOptions));
            _registry.Register(metadata, instancePath);
        }

        public void OpenInExplorer(string folderName)
        {
            string folderPath = _pathService.GetInstancePath(folderName);
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
            string folderPath = _pathService.GetInstancePath(folderName);
            if (Directory.Exists(folderPath))
            {
                FileUtils.AtomicWriteAllText(_pathService.GetEulaPath(folderPath),
                    "# By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n" +
                    "eula=true\n");
            }
        }

        private async Task<bool> DeleteDirectoryWithRetryAsync(string folderPath, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(folderPath)) return true;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await FileUtils.CleanDirectoryAsync(folderPath, cancellationToken);
                    return true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Delete attempt {Attempt} failed for {FolderPath}.", attempt, folderPath);
                    if (attempt == 3) return false;
                    await Task.Delay(500, cancellationToken);
                }
            }
            return false;
        }
    }
