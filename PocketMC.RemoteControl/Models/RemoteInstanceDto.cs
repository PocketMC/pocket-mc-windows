using PocketMC.Domain.Models;
namespace PocketMC.RemoteControl.Models;

public sealed class RemoteInstanceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public string State { get; set; } = "Offline";
    public string MinecraftVersion { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
}
