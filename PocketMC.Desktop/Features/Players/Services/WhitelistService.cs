using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Helpers;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Players.Services;

public sealed class WhitelistEntry
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Reads and writes whitelist.json for offline whitelist manipulation.
/// For online operations, commands should be sent via ServerRuntimeSettingApplier.
/// </summary>
public sealed class WhitelistService
{
    private readonly InstanceRegistry _registry;
    private readonly ILogger<WhitelistService> _logger;

    public WhitelistService(InstanceRegistry registry, ILogger<WhitelistService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<List<WhitelistEntry>> GetWhitelistedPlayersAsync(InstanceMetadata instance)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot))
            return new List<WhitelistEntry>();

        try
        {
            string path = GetWhitelistPath(serverRoot, instance);
            return await ReadWhitelistAsync(path, instance);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read whitelist for instance {InstanceId}.", instance.Id);
            return new List<WhitelistEntry>();
        }
    }

    public async Task AddPlayerAsync(InstanceMetadata instance, string username)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot)) return;

        string path = GetWhitelistPath(serverRoot, instance);
        var entries = await ReadWhitelistAsync(path, instance);

        if (entries.Any(e => string.Equals(e.Name, username, StringComparison.OrdinalIgnoreCase)))
            return; // Already whitelisted

        entries.Add(new WhitelistEntry
        {
            Uuid = GenerateOfflineUuid(username),
            Name = username
        });

        await WriteWhitelistAsync(path, entries, instance);
    }

    public async Task RemovePlayerAsync(InstanceMetadata instance, string username)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot)) return;

        string path = GetWhitelistPath(serverRoot, instance);
        var entries = await ReadWhitelistAsync(path, instance);
        entries.RemoveAll(e => string.Equals(e.Name, username, StringComparison.OrdinalIgnoreCase));
        await WriteWhitelistAsync(path, entries, instance);
    }

    private static string GetWhitelistPath(string serverRoot, InstanceMetadata instance)
    {
        // PocketMine uses white-list.txt, Java/BDS use whitelist.json
        if (CommandFormatter.IsPocketMine(instance.ServerType))
            return Path.Combine(serverRoot, "white-list.txt");

        return Path.Combine(serverRoot, "whitelist.json");
    }

    private static async Task<List<WhitelistEntry>> ReadWhitelistAsync(string path, InstanceMetadata instance)
    {
        if (!File.Exists(path))
            return new List<WhitelistEntry>();

        if (CommandFormatter.IsPocketMine(instance.ServerType))
        {
            // PocketMine: plain text, one name per line
            string text = await ReadTextSafeAsync(path);
            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                .Select(name => new WhitelistEntry { Name = name, Uuid = string.Empty })
                .ToList();
        }

        // Java / BDS: JSON array
        string json = await ReadTextSafeAsync(path);
        if (string.IsNullOrWhiteSpace(json))
            return new List<WhitelistEntry>();

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return new List<WhitelistEntry>();

        var entries = new List<WhitelistEntry>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            string name = TryGetString(element, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            entries.Add(new WhitelistEntry
            {
                Uuid = TryGetString(element, "uuid"),
                Name = name
            });
        }

        return entries;
    }

    private static async Task WriteWhitelistAsync(string path, List<WhitelistEntry> entries, InstanceMetadata instance)
    {
        if (CommandFormatter.IsPocketMine(instance.ServerType))
        {
            var lines = entries.Select(e => e.Name);
            await File.WriteAllTextAsync(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            return;
        }

        // Java / BDS: JSON array
        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonEntries = entries.Select(e => new Dictionary<string, string>
        {
            { "uuid", e.Uuid },
            { "name", e.Name }
        }).ToArray();

        string json = JsonSerializer.Serialize(jsonEntries, options);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }

    /// <summary>
    /// Generates an offline-mode UUID from a username using the standard Minecraft algorithm.
    /// This is MD5("OfflinePlayer:" + name) with version 3 UUID bits set.
    /// </summary>
    internal static string GenerateOfflineUuid(string username)
    {
        byte[] data = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));

        // Set version to 3 (name-based, MD5)
        data[6] = (byte)((data[6] & 0x0F) | 0x30);
        // Set variant to IETF
        data[8] = (byte)((data[8] & 0x3F) | 0x80);

        return new Guid(data).ToString();
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static async Task<string> ReadTextSafeAsync(string path)
    {
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
        }

        return string.Empty;
    }
}
