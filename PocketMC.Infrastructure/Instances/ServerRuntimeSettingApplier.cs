using PocketMC.Application.Services.Players;
using PocketMC.Application.Services.Instances;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using PocketMC.Application.Interfaces.Instances;

namespace PocketMC.Infrastructure.Instances;

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
    /// Player name is validated before being sent to the live command channel.
    /// </summary>
    public Task ApplyWhitelistAddAsync(Guid instanceId, string playerName)
    {
        if (!TryFormatCommandPlayerName(instanceId, playerName, out string formattedName))
        {
            return Task.CompletedTask;
        }

        string prefix = GetWhitelistCommandPrefix(instanceId);
        return SendIfOnlineAsync(instanceId, $"{prefix} add {formattedName}");
    }

    /// <summary>
    /// Remove a player from the whitelist/allowlist by name. Requires server online.
    /// Java/PocketMine: "whitelist remove". BDS: "allowlist remove".
    /// Player name is validated before being sent to the live command channel.
    /// </summary>
    public Task ApplyWhitelistRemoveAsync(Guid instanceId, string playerName)
    {
        if (!TryFormatCommandPlayerName(instanceId, playerName, out string formattedName))
        {
            return Task.CompletedTask;
        }

        string prefix = GetWhitelistCommandPrefix(instanceId);
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

    private bool TryFormatCommandPlayerName(Guid instanceId, string playerName, out string formattedName)
    {
        InstanceMetadata? metadata = _instanceRegistry.GetById(instanceId);
        if (metadata == null)
        {
            formattedName = string.Empty;
            _logger.LogWarning("Skipping live player command for unknown instance {InstanceId}.", instanceId);
            return false;
        }

        if (!CommandFormatter.TryFormatPlayerName(playerName, metadata.ServerType, out formattedName))
        {
            _logger.LogWarning(
                "Skipping live player command for instance {InstanceId} because player name '{PlayerName}' is not valid for server type {ServerType}.",
                instanceId,
                playerName,
                metadata.ServerType);
            return false;
        }

        return true;
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

