using System;

namespace PocketMC.Domain.Models;

public sealed class RemoteControlUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? ProtectedPassword { get; set; }

    // Permissions
    public bool AllowRemoteConsoleCommands { get; set; }
    public bool AllowRemotePlayerActions { get; set; }
    public bool AllowRemoteServerSettings { get; set; }
    public bool AllowRemoteServerAddons { get; set; }
    public bool AllowRemoteFileManager { get; set; }
    public bool AllowRemoteServerBackups { get; set; }

    // Instance Access
    public bool AllowAllInstances { get; set; } = true;
    public List<Guid> AllowedInstanceIds { get; set; } = new();

    public bool CanAccessInstance(Guid instanceId)
    {
        return AllowAllInstances || (AllowedInstanceIds != null && AllowedInstanceIds.Contains(instanceId));
    }
}
