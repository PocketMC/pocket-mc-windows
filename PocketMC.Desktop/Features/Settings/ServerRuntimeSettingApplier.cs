using PocketMC.Desktop.Features.Console;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Helpers;

namespace PocketMC.Desktop.Features.Settings;

/// <summary>
/// Sends live setting-change commands to a running server process.
/// If the server is offline or the command is not supported for the engine,
/// this service does nothing — the caller should still persist to config files.
/// </summary>
public sealed class ServerRuntimeSettingApplier
{
    private readonly ServerProcessManager _processManager;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly ILogger<ServerRuntimeSettingApplier> _logger;

    public ServerRuntimeSettingApplier(
        ServerProcessManager processManager,
        InstanceRegistry instanceRegistry,
        ILogger<ServerRuntimeSettingApplier> logger)
    {
        _processManager = processManager;
        _instanceRegistry = instanceRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Apply difficulty live. Supported on Java (vanilla, Spigot, etc.) and BDS.
    /// PocketMine uses "difficulty" too.
    /// </summary>
    public Task ApplyDifficultyAsync(Guid instanceId, string difficulty)
        => SendIfOnlineAsync(instanceId, $"difficulty {difficulty}");

    /// <summary>
    /// Toggle whitelist/allowlist on/off.
    /// Java/PocketMine: "whitelist on|off". BDS: "allowlist on|off".
    /// </summary>
    public Task ApplyWhitelistToggleAsync(Guid instanceId, bool enabled)
    {
        string prefix = GetWhitelistCommandPrefix(instanceId);
        return SendIfOnlineAsync(instanceId, $"{prefix} {(enabled ? "on" : "off")}");
    }

    /// <summary>
    /// Reload whitelist/allowlist from disk.
    /// Java/PocketMine: "whitelist reload". BDS: "allowlist reload".
    /// </summary>
    public Task ApplyWhitelistReloadAsync(Guid instanceId)
    {
        string prefix = GetWhitelistCommandPrefix(instanceId);
        return SendIfOnlineAsync(instanceId, $"{prefix} reload");
    }

    /// <summary>
    /// Add a player to the whitelist/allowlist by name. Requires server online.
    /// Java/PocketMine: "whitelist add". BDS: "allowlist add".
    /// Player name is quoted only for Bedrock to handle spaces (e.g., gamertags).
    /// </summary>
    public Task ApplyWhitelistAddAsync(Guid instanceId, string playerName)
    {
        bool isBedrock = IsBedrock(instanceId);
        string prefix = isBedrock ? "allowlist" : "whitelist";
        string formattedName = isBedrock ? QuotePlayerName(playerName) : playerName;
        return SendIfOnlineAsync(instanceId, $"{prefix} add {formattedName}");
    }

    /// <summary>
    /// Remove a player from the whitelist/allowlist by name. Requires server online.
    /// Java/PocketMine: "whitelist remove". BDS: "allowlist remove".
    /// Player name is quoted only for Bedrock to handle spaces (e.g., gamertags).
    /// </summary>
    public Task ApplyWhitelistRemoveAsync(Guid instanceId, string playerName)
    {
        bool isBedrock = IsBedrock(instanceId);
        string prefix = isBedrock ? "allowlist" : "whitelist";
        string formattedName = isBedrock ? QuotePlayerName(playerName) : playerName;
        return SendIfOnlineAsync(instanceId, $"{prefix} remove {formattedName}");
    }



    /// <summary>
    /// Apply default gamemode live. Supported on Java and BDS.
    /// </summary>
    public Task ApplyDefaultGamemodeAsync(Guid instanceId, string gamemode)
        => SendIfOnlineAsync(instanceId, $"defaultgamemode {gamemode}");

    private bool IsBedrock(Guid instanceId)
    {
        InstanceMetadata? metadata = _instanceRegistry.GetById(instanceId);
        return metadata != null && CommandFormatter.IsBedrock(metadata.ServerType);
    }

    /// <summary>
    /// Returns "allowlist" for Bedrock Dedicated Server, "whitelist" for all other engines.
    /// </summary>
    private string GetWhitelistCommandPrefix(Guid instanceId)
    {
        return IsBedrock(instanceId) ? "allowlist" : "whitelist";
    }

    /// <summary>
    /// Wraps a player name in double quotes, escaping any embedded quotes or backslashes.
    /// Safe for all server types — Minecraft servers universally accept quoted names.
    /// </summary>
    private static string QuotePlayerName(string playerName)
    {
        string escaped = playerName
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private async Task SendIfOnlineAsync(Guid instanceId, string command)
    {
        ServerProcess? process = _processManager.GetProcess(instanceId);
        if (process == null || process.State != ServerState.Online)
        {
            _logger.LogDebug("Server {InstanceId} is not running — skipping live command: {Command}", instanceId, command);
            return;
        }

        try
        {
            _logger.LogInformation("Sending live command to {InstanceId}: {Command}", instanceId, command);
            await process.WriteInputAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send live command '{Command}' to server {InstanceId}.", command, instanceId);
        }
    }
}

