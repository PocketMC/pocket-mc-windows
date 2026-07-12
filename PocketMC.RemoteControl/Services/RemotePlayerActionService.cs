using PocketMC.RemoteControl.Models;
using PocketMC.Application.Services.Players;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Infrastructure.Players;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Shell;

namespace PocketMC.RemoteControl.Services;

public sealed class RemotePlayerActionService
{
    private readonly ApplicationState _applicationState;
    private readonly InstanceRegistry _registry;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly RemoteAuditLogService _auditLogService;

    public RemotePlayerActionService(
        ApplicationState applicationState,
        InstanceRegistry registry,
        IServerLifecycleService lifecycleService,
        RemoteAuditLogService auditLogService)
    {
        _applicationState = applicationState;
        _registry = registry;
        _lifecycleService = lifecycleService;
        _auditLogService = auditLogService;
    }

    public async Task<RemoteControlActionResult> ExecuteAsync(
        Guid instanceId,
        string playerName,
        string action,
        RemotePlayerActionRequest? request,
        string? deviceId)
    {
        if (!_applicationState.Settings.RemoteControl.AllowRemotePlayerActions)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Disabled, "Remote player actions are disabled.");
        }

        InstanceMetadata? metadata = _registry.GetById(instanceId);
        if (metadata == null)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.NotFound, "Instance was not found.");
        }

        if (!CommandFormatter.TryFormatPlayerName(playerName, metadata.ServerType, out string formattedName))
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, "Invalid player name.");
        }

        if (request?.Reason != null && (request.Reason.Length > 255 || request.Reason.Any(c => char.IsControl(c) || c == '\r' || c == '\n')))
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, "Invalid reason format.");
        }

        var process = _lifecycleService.GetProcess(instanceId);
        if (process == null || process.State != ServerState.Online)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.NotRunning, "Instance is not fully online.");
        }

        IReadOnlyList<string> commands = BuildCommands(action, formattedName, metadata.ServerType, request?.Reason);
        if (commands.Count == 0)
        {
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, "This action is not supported for this server type.");
        }

        try
        {
            foreach (string command in commands)
            {
                await process.WriteInputAsync(command);
            }

            _auditLogService.Log(deviceId, $"player.{action}", instanceId, playerName);
            return RemoteControlActionResult.Successful();
        }
        catch (Exception ex)
        {
            _auditLogService.Log(deviceId, $"player.{action}", instanceId, playerName, success: false, ex.Message);
            return RemoteControlActionResult.Failed(RemoteControlActionFailure.Failed, ex.Message);
        }
    }

    private static IReadOnlyList<string> BuildCommands(string action, string formattedName, string? serverType, string? reason) =>
        action.ToLowerInvariant() switch
        {
            "kick" => PlayerActionCommandBuilder.BuildSubmitCommands("Kick", formattedName, serverType, reason),
            "ban" => PlayerActionCommandBuilder.BuildSubmitCommands("Ban", formattedName, serverType, reason),
            "pardon" => PlayerActionCommandBuilder.BuildPardonCommands(formattedName, serverType),
            "op" => new[] { $"op {formattedName}" },
            "deop" => new[] { $"deop {formattedName}" },
            _ => Array.Empty<string>()
        };

}

