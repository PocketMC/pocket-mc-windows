
using System;
using System.Collections.Generic;
using PocketMC.Application.Interfaces.Instances;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Players;

public static class PlayerActionCommandBuilder
{
    public static IReadOnlyList<string> BuildSubmitCommands(
        string action,
        string formattedName,
        string? serverType,
        string? reason)
    {
        string commandName = action.Equals("Kick", StringComparison.OrdinalIgnoreCase) ? "kick" : "ban";
        if (commandName == "kick")
        {
            return new[] { CommandFormatter.AppendOptionalReason($"kick {formattedName}", reason) };
        }

        if (CommandFormatter.IsBedrock(serverType))
        {
            return new[] { CommandFormatter.AppendOptionalReason($"kick {formattedName}", reason) };
        }

        return new[]
        {
            CommandFormatter.AppendOptionalReason($"ban {formattedName}", reason),
            CommandFormatter.AppendOptionalReason($"kick {formattedName}", reason)
        };
    }

    public static IReadOnlyList<string> BuildPardonCommands(string formattedName, string? serverType)
    {
        if (CommandFormatter.IsBedrock(serverType))
        {
            return Array.Empty<string>();
        }

        if (CommandFormatter.IsPocketMine(serverType))
        {
            return new[] { $"unban {formattedName}" };
        }

        return new[] { $"pardon {formattedName}" };
    }
}
