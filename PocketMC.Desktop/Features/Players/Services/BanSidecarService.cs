using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Players.Services;

public sealed class BanSidecarService
{
    public const string FileName = "pocketmc-bans.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly InstanceRegistry _registry;
    private readonly ILogger<BanSidecarService> _logger;

    public BanSidecarService(
        InstanceRegistry registry,
        ILogger<BanSidecarService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<List<BannedPlayerEntry>> GetBannedPlayersAsync(InstanceMetadata instance)
    {
        List<BedrockBanSidecarEntry> entries = await ReadEntriesAsync(instance);
        return entries.Select(entry => new BannedPlayerEntry
        {
            Name = entry.Name,
            Reason = entry.Reason,
            Created = entry.BannedAt.ToUniversalTime().ToString("O"),
            Expires = "forever",
            IsSidecar = true
        }).ToList();
    }

    public async Task AddBanAsync(InstanceMetadata instance, string name, string reason)
    {
        List<BedrockBanSidecarEntry> entries = await ReadEntriesAsync(instance);
        entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        entries.Add(new BedrockBanSidecarEntry
        {
            Name = name,
            Xuid = string.Empty,
            BannedAt = DateTime.UtcNow,
            Reason = reason,
            BannedBy = "console"
        });

        await WriteEntriesAsync(instance, entries);
    }

    public async Task RemoveBanAsync(InstanceMetadata instance, string name)
    {
        List<BedrockBanSidecarEntry> entries = await ReadEntriesAsync(instance);
        int removed = entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            await WriteEntriesAsync(instance, entries);
        }
    }

    private async Task<List<BedrockBanSidecarEntry>> ReadEntriesAsync(InstanceMetadata instance)
    {
        string? path = GetPath(instance);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new List<BedrockBanSidecarEntry>();
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            List<BedrockBanSidecarEntry>? entries = await JsonSerializer.DeserializeAsync<List<BedrockBanSidecarEntry>>(stream, JsonOptions);
            return entries ?? new List<BedrockBanSidecarEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Bedrock ban sidecar for instance {InstanceId}.", instance.Id);
            return new List<BedrockBanSidecarEntry>();
        }
    }

    private async Task WriteEntriesAsync(InstanceMetadata instance, List<BedrockBanSidecarEntry> entries)
    {
        string? path = GetPath(instance);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions);
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
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string? GetPath(InstanceMetadata instance)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        return string.IsNullOrWhiteSpace(serverRoot)
            ? null
            : Path.Combine(serverRoot, FileName);
    }

    private sealed class BedrockBanSidecarEntry
    {
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("xuid")]
        public string Xuid { get; set; } = string.Empty;

        public DateTime BannedAt { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string BannedBy { get; set; } = "console";
    }
}
