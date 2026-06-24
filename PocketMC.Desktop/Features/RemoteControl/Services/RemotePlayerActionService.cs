using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Players.Services;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Helpers;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Features.RemoteControl.Services;

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

        if (!IsValidPlayerName(playerName, metadata.ServerType))
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

        string formattedName = CommandFormatter.FormatPlayerName(playerName, metadata.ServerType);
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

    private static bool IsValidPlayerName(string name, string? serverType)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > 100) return false;

        if (name.Any(c => char.IsControl(c) || c == '\r' || c == '\n' || c == '\t' || c == '"' || c == '\''))
        {
            return false;
        }

        if (CommandFormatter.IsBedrock(serverType))
        {
            return true;
        }
        else
        {
            return Regex.IsMatch(name, @"^[A-Za-z0-9_]{1,16}$");
        }
    }
}

