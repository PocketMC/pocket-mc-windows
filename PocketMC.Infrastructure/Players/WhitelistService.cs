using PocketMC.Application.Services.Players;
using PocketMC.Application.Services.Instances;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.Instances;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Players;

public enum WhitelistAddResult
{
    AddedWithOnlineUuid,
    AddedWithOfflineUuidFallback,
    AddedWithOfflineUuid,
    AlreadyExists
}

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
    private readonly ServerConfigurationService _configService;
    private readonly ILogger<WhitelistService> _logger;
    private static readonly HttpClient _httpClient = new();

    public WhitelistService(InstanceRegistry registry, ServerConfigurationService configService, ILogger<WhitelistService> logger)
    {
        _registry = registry;
        _configService = configService;
        _logger = logger;
    }

    private async Task<string?> FetchOnlineUuidAsync(string username)
    {
        try
        {
            string url = $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(username)}";
            using var response = await _httpClient.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                string rawId = idProp.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(rawId))
                {
                    return Guid.Parse(rawId).ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve online UUID for Mojang account {Username}", username);
        }
        return null;
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

    public async Task<WhitelistAddResult> AddPlayerAsync(InstanceMetadata instance, string username)
    {
        string? serverRoot = _registry.GetPath(instance.Id);
        if (string.IsNullOrWhiteSpace(serverRoot))
            return WhitelistAddResult.AddedWithOfflineUuid;

        string path = GetWhitelistPath(serverRoot, instance);
        var entries = await ReadWhitelistAsync(path, instance);

        if (entries.Any(e => string.Equals(e.Name, username, StringComparison.OrdinalIgnoreCase)))
            return WhitelistAddResult.AlreadyExists;

        bool isJava = !CommandFormatter.IsBedrock(instance.ServerType) && !CommandFormatter.IsPocketMine(instance.ServerType);
        bool onlineMode = false;
        if (isJava && _configService.TryGetProperty(serverRoot, "online-mode", out string? onlineModeStr))
        {
            onlineMode = string.Equals(onlineModeStr, "true", StringComparison.OrdinalIgnoreCase);
        }

        string uuid = string.Empty;
        WhitelistAddResult result;

        if (isJava && onlineMode)
        {
            string? onlineUuid = await FetchOnlineUuidAsync(username);
            if (onlineUuid != null)
            {
                uuid = onlineUuid;
                result = WhitelistAddResult.AddedWithOnlineUuid;
            }
            else
            {
                uuid = GenerateOfflineUuid(username);
                result = WhitelistAddResult.AddedWithOfflineUuidFallback;
            }
        }
        else
        {
            uuid = GenerateOfflineUuid(username);
            result = WhitelistAddResult.AddedWithOfflineUuid;
        }

        entries.Add(new WhitelistEntry
        {
            Uuid = uuid,
            Name = username
        });

        await WriteWhitelistAsync(path, entries, instance);
        return result;
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
        // PocketMine uses white-list.txt
        if (CommandFormatter.IsPocketMine(instance.ServerType))
            return Path.Combine(serverRoot, "white-list.txt");

        // Bedrock Dedicated Server uses allowlist.json
        if (CommandFormatter.IsBedrock(instance.ServerType))
            return Path.Combine(serverRoot, "allowlist.json");

        // Java servers use whitelist.json
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

        // JSON array format (both Bedrock and Java)
        string json = await ReadTextSafeAsync(path);
        if (string.IsNullOrWhiteSpace(json))
            return new List<WhitelistEntry>();

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return new List<WhitelistEntry>();

        bool isBedrock = CommandFormatter.IsBedrock(instance.ServerType);
        var entries = new List<WhitelistEntry>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            string name = TryGetString(element, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Bedrock allowlist.json uses "xuid"; Java whitelist.json uses "uuid"
            string identifier = isBedrock
                ? TryGetString(element, "xuid")
                : TryGetString(element, "uuid");

            entries.Add(new WhitelistEntry
            {
                Uuid = identifier,
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

        var options = new JsonSerializerOptions { WriteIndented = true };

        if (CommandFormatter.IsBedrock(instance.ServerType))
        {
            // Bedrock allowlist.json format: [{"ignoresPlayerLimit":false,"name":"PlayerName","xuid":"..."}]
            var jsonEntries = entries.Select(e => new Dictionary<string, object>
            {
                { "ignoresPlayerLimit", false },
                { "name", e.Name },
                { "xuid", e.Uuid }
            }).ToArray();

            string json = JsonSerializer.Serialize(jsonEntries, options);
            await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
            return;
        }

        // Java: whitelist.json format: [{"uuid":"...","name":"..."}]
        var javaEntries = entries.Select(e => new Dictionary<string, string>
        {
            { "uuid", e.Uuid },
            { "name", e.Name }
        }).ToArray();

        string javaJson = JsonSerializer.Serialize(javaEntries, options);
        await File.WriteAllTextAsync(path, javaJson, new UTF8Encoding(false));
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
