using PocketMC.Domain.Models;
namespace PocketMC.RemoteControl.Models;

public sealed class RemoteInstanceStatusDto
{
    public Guid InstanceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public string State { get; set; } = "Offline";
    public string MinecraftVersion { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public int UptimeSeconds { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public IReadOnlyList<RemotePlayerDto> OnlinePlayers { get; set; } = Array.Empty<RemotePlayerDto>();
    public IReadOnlyList<string> OppedPlayers { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> BannedPlayers { get; set; } = Array.Empty<string>();
    public double CpuUsage { get; set; }
    public double RamUsageMb { get; set; }
    public int MaxRamMb { get; set; }
    public IReadOnlyList<ServerIpDto> ServerIps { get; set; } = Array.Empty<ServerIpDto>();
}
