using PocketMC.Domain.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Storage;
using PocketMC.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PocketMC.Application.Services.Instances;

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
    private readonly IServiceProvider _serviceProvider;

    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    public InstanceManager(
        InstanceRegistry registry,
        InstancePathService pathService,
        ApplicationState applicationState,
        IAssetProvider assetProvider,
        ILogger<InstanceManager> logger,
        IServiceProvider serviceProvider)
    {
        _registry = registry;
        _pathService = pathService;
        _applicationState = applicationState;
        _assetProvider = assetProvider;
        _logger = logger;
        _serviceProvider = serviceProvider;
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
        _ = _serviceProvider.GetService<ITelemetryService>()?.ReportServerActionAsync("create");
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

    public string RenameInstance(Guid instanceId, string newName, string newDescription)
    {
        string? oldPath = _registry.GetPath(instanceId);
        if (string.IsNullOrEmpty(oldPath) || !Directory.Exists(oldPath))
        {
            throw new DirectoryNotFoundException($"Instance path for {instanceId} not found.");
        }

        string oldSlug = Path.GetFileName(oldPath);
        string baseSlug = SlugHelper.GenerateSlug(newName);
        string newSlug = baseSlug;
        int counter = 2;

        // Collision detection (excluding the current directory even if case matches)
        while (Directory.Exists(_pathService.GetInstancePath(newSlug)) &&
               !string.Equals(newSlug, oldSlug, StringComparison.OrdinalIgnoreCase))
        {
            newSlug = $"{baseSlug}-{counter}";
            counter++;
        }

        string finalPath = oldPath;

        // Only perform folder move if the slug actually changed (including case changes)
        if (newSlug != oldSlug)
        {
            string newPath = _pathService.GetInstancePath(newSlug);

            try
            {
                // Windows is case-insensitive. If it's a case-only rename, we need a 3-step move.
                if (string.Equals(newSlug, oldSlug, StringComparison.OrdinalIgnoreCase))
                {
                    string tempPath = newPath + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                    Directory.Move(oldPath, tempPath);
                    try
                    {
                        Directory.Move(tempPath, newPath);
                    }
                    catch
                    {
                        Directory.Move(tempPath, oldPath);
                        throw;
                    }
                }
                else
                {
                    Directory.Move(oldPath, newPath);
                }
                finalPath = newPath;
                _logger.LogInformation("Renamed instance folder from {OldPath} to {NewPath}", oldPath, newPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename instance folder {OldPath} to {NewPath}", oldPath, newPath);
                throw new IOException($"Failed to rename server folder. Ensure the server is stopped and no files are open in other programs.", ex);
            }
        }

        // Update Metadata
        var metadata = _registry.GetById(instanceId);
        if (metadata != null)
        {
            metadata.Name = newName;
            metadata.Description = newDescription;
            SaveMetadata(metadata, finalPath);
        }

        return finalPath;
    }

    [Obsolete("Use RenameInstance(Guid, string, string) for safer atomic renames.")]
    public void UpdateMetadata(string folderName, string newName, string newDescription)
    {
        var metadata = _registry.GetAll().FirstOrDefault(m => string.Equals(Path.GetFileName(_registry.GetPath(m.Id)), folderName, StringComparison.OrdinalIgnoreCase));
        if (metadata != null)
        {
            RenameInstance(metadata.Id, newName, newDescription);
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
            _ = _serviceProvider.GetService<ITelemetryService>()?.ReportServerActionAsync("delete");
        }

        return deleted;
    }

    public async Task<bool> DeleteInstanceAsync(string folderName, CancellationToken cancellationToken = default)
    {
        string folderPath = _pathService.GetInstancePath(folderName);
        bool deleted = await DeleteDirectoryWithRetryAsync(folderPath, cancellationToken);
        if (deleted)
        {
            _ = _serviceProvider.GetService<ITelemetryService>()?.ReportServerActionAsync("delete");
        }
        return deleted;
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

