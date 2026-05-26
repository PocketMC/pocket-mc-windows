using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Settings;

/// <summary>
/// Sends live setting-change commands to a running server process.
/// If the server is offline or the command is not supported for the engine,
/// this service does nothing — the caller should still persist to config files.
/// </summary>
public sealed class ServerRuntimeSettingApplier
{
    private readonly ServerProcessManager _processManager;
    private readonly ILogger<ServerRuntimeSettingApplier> _logger;

    public ServerRuntimeSettingApplier(
        ServerProcessManager processManager,
        ILogger<ServerRuntimeSettingApplier> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    /// <summary>
    /// Apply difficulty live. Supported on Java (vanilla, Spigot, etc.) and BDS.
    /// PocketMine uses "difficulty" too.
    /// </summary>
    public Task ApplyDifficultyAsync(Guid instanceId, string difficulty)
        => SendIfOnlineAsync(instanceId, $"difficulty {difficulty}");

    /// <summary>
    /// Toggle whitelist on/off. Java: "whitelist on|off". BDS: same syntax.
    /// PocketMine: "whitelist on|off" also works.
    /// </summary>
    public Task ApplyWhitelistToggleAsync(Guid instanceId, bool enabled)
        => SendIfOnlineAsync(instanceId, $"whitelist {(enabled ? "on" : "off")}");

    /// <summary>
    /// Reload whitelist from disk. Java/BDS/PocketMine all support "whitelist reload".
    /// </summary>
    public Task ApplyWhitelistReloadAsync(Guid instanceId)
        => SendIfOnlineAsync(instanceId, "whitelist reload");

    /// <summary>
    /// Add a player to the whitelist by name. Requires server online.
    /// </summary>
    public Task ApplyWhitelistAddAsync(Guid instanceId, string playerName)
        => SendIfOnlineAsync(instanceId, $"whitelist add {playerName}");

    /// <summary>
    /// Remove a player from the whitelist by name. Requires server online.
    /// </summary>
    public Task ApplyWhitelistRemoveAsync(Guid instanceId, string playerName)
        => SendIfOnlineAsync(instanceId, $"whitelist remove {playerName}");

    /// <summary>
    /// Apply a gamerule change. Only supported on Java and BDS (not PocketMine).
    /// </summary>
    public Task ApplyGameruleAsync(Guid instanceId, EngineFamily family, string rule, string value)
    {
        if (family == EngineFamily.Pocketmine)
        {
            _logger.LogDebug("Skipping gamerule {Rule} — PocketMine does not support gamerule commands.", rule);
            return Task.CompletedTask;
        }

        return SendIfOnlineAsync(instanceId, $"gamerule {rule} {value}");
    }

    /// <summary>
    /// Set weather. Java: "weather clear|rain|thunder". BDS: same syntax.
    /// PocketMine does not support the weather command natively.
    /// </summary>
    public Task ApplyWeatherAsync(Guid instanceId, EngineFamily family, string weather)
    {
        if (family == EngineFamily.Pocketmine)
        {
            _logger.LogDebug("Skipping weather command — PocketMine does not support it natively.");
            return Task.CompletedTask;
        }

        return SendIfOnlineAsync(instanceId, $"weather {weather}");
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
