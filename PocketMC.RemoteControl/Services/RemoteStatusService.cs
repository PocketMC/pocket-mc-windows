using PocketMC.RemoteControl.Models;
using PocketMC.Application.Services.Players;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Shell;

namespace PocketMC.RemoteControl.Services;

public sealed class RemoteStatusService
{
    private readonly InstanceRegistry _registry;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly IResourceMonitorService _resourceMonitorService;
    private readonly LocalNetworkAddressService _localNetworkAddressService;
    private readonly ApplicationState _applicationState;
    private readonly PocketMC.Infrastructure.Players.ServerStateFileService _serverStateFileService;
    private readonly PocketMC.Application.Interfaces.Instances.IGeyserDetector _geyserDetector;

    public RemoteStatusService(
        InstanceRegistry registry,
        IServerLifecycleService lifecycleService,
        IResourceMonitorService resourceMonitorService,
        LocalNetworkAddressService localNetworkAddressService,
        ApplicationState applicationState,
        PocketMC.Infrastructure.Players.ServerStateFileService serverStateFileService,
        PocketMC.Application.Interfaces.Instances.IGeyserDetector geyserDetector)
    {
        _registry = registry;
        _lifecycleService = lifecycleService;
        _resourceMonitorService = resourceMonitorService;
        _localNetworkAddressService = localNetworkAddressService;
        _applicationState = applicationState;
        _serverStateFileService = serverStateFileService;
        _geyserDetector = geyserDetector;
    }

    public IReadOnlyList<RemoteInstanceDto> GetInstances() =>
        _registry.GetAll()
            .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .Select(instance =>
            {
                bool isRunning = _lifecycleService.IsRunning(instance.Id);
                var process = _lifecycleService.GetProcess(instance.Id);
                _resourceMonitorService.Metrics.TryGetValue(instance.Id, out InstanceMetrics? metrics);
                return new RemoteInstanceDto
                {
                    Id = instance.Id,
                    Name = instance.Name,
                    ServerType = instance.ServerType,
                    IsRunning = isRunning,
                    State = GetState(instance.Id),
                    MinecraftVersion = instance.MinecraftVersion,
                    PlayerCount = process?.PlayerCount ?? metrics?.PlayerCount ?? 0,
                    MaxPlayers = instance.MaxPlayers
                };
            })
            .ToList();

    public async System.Threading.Tasks.Task<RemoteInstanceStatusDto?> GetInstanceStatusAsync(Guid instanceId)
    {
        InstanceMetadata? metadata = _registry.GetById(instanceId);
        if (metadata == null)
        {
            return null;
        }

        bool isRunning = _lifecycleService.IsRunning(instanceId);
        DateTime? sessionStartUtc = _lifecycleService.GetSessionStartTime(instanceId);
        int uptimeSeconds = isRunning && sessionStartUtc.HasValue
            ? Math.Max(0, (int)(DateTime.UtcNow - sessionStartUtc.Value).TotalSeconds)
            : 0;

        _resourceMonitorService.Metrics.TryGetValue(instanceId, out InstanceMetrics? metrics);
        var process = _lifecycleService.GetProcess(instanceId);
        IReadOnlyList<string> onlinePlayerNames = process?.OnlinePlayerNames ?? Array.Empty<string>();

        var oppedPlayersList = await _serverStateFileService.GetOppedPlayersAsync(metadata);
        var oppedPlayers = oppedPlayersList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bannedPlayersList = await _serverStateFileService.GetBannedPlayersAsync(metadata);
        var bannedPlayersNames = bannedPlayersList.Select(b => b.Name).ToList();

        var onlinePlayers = onlinePlayerNames.Select(name => new RemotePlayerDto
        {
            Name = name,
            IsOp = oppedPlayers.Contains(name)
        }).ToList();

        int serverPort = metadata.ServerPort ?? (_geyserDetector.IsGeyserInstalled(_registry.GetPath(metadata.Id)) && metadata.GeyserBedrockPort.HasValue ? metadata.GeyserBedrockPort.Value : 25565);
        var serverIps = new List<ServerIpDto>();

        string? tunnelIp = _applicationState.GetTunnelAddress(instanceId);
        if (!string.IsNullOrWhiteSpace(tunnelIp))
        {
            serverIps.Add(new ServerIpDto { Label = PocketMC.Application.Services.Players.CommandFormatter.IsBedrock(metadata.ServerType) ? "Primary (Playit)" : "Java (Playit)", Address = tunnelIp });
        }

        string? bedrockTunnelIp = _applicationState.GetBedrockTunnelAddress(instanceId);
        if (!string.IsNullOrWhiteSpace(bedrockTunnelIp) && bedrockTunnelIp != tunnelIp)
        {
            serverIps.Add(new ServerIpDto { Label = "Bedrock (Playit)", Address = bedrockTunnelIp });
        }

        string? bedrockNumericTunnelIp = _applicationState.GetBedrockNumericTunnelAddress(instanceId);
        if (!string.IsNullOrWhiteSpace(bedrockNumericTunnelIp) && bedrockNumericTunnelIp != bedrockTunnelIp)
        {
            serverIps.Add(new ServerIpDto { Label = "Bedrock Numeric (Playit)", Address = bedrockNumericTunnelIp });
        }

        string? voiceChatTunnelIp = _applicationState.GetVoiceChatTunnelAddress(instanceId);
        if (!string.IsNullOrWhiteSpace(voiceChatTunnelIp))
        {
            serverIps.Add(new ServerIpDto { Label = "Voice Chat", Address = voiceChatTunnelIp });
        }

        string? localIp = _localNetworkAddressService.GetLocalIpAddresses().FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(localIp))
        {
            serverIps.Add(new ServerIpDto { Label = PocketMC.Application.Services.Players.CommandFormatter.IsBedrock(metadata.ServerType) ? "LAN" : "Java (LAN)", Address = $"{localIp}:{serverPort}" });

            if (_geyserDetector.IsGeyserInstalled(_registry.GetPath(metadata.Id)) && metadata.GeyserBedrockPort.HasValue)
            {
                serverIps.Add(new ServerIpDto { Label = "Bedrock (LAN)", Address = $"{localIp}:{metadata.GeyserBedrockPort.Value}" });
            }
        }

        return new RemoteInstanceStatusDto
        {
            InstanceId = metadata.Id,
            Name = metadata.Name,
            ServerType = metadata.ServerType,
            State = GetState(instanceId),
            MinecraftVersion = metadata.MinecraftVersion,
            IsRunning = isRunning,
            UptimeSeconds = uptimeSeconds,
            PlayerCount = process?.PlayerCount ?? metrics?.PlayerCount ?? 0,
            MaxPlayers = metadata.MaxPlayers,
            OnlinePlayers = onlinePlayers,
            OppedPlayers = oppedPlayersList,
            BannedPlayers = bannedPlayersNames,
            CpuUsage = metrics?.CpuUsage ?? 0,
            RamUsageMb = metrics?.RamUsageMb ?? 0,
            MaxRamMb = metadata.MaxRamMb,
            ServerIps = serverIps
        };
    }

    private string GetState(Guid instanceId)
    {
        var process = _lifecycleService.GetProcess(instanceId);
        if (process != null)
        {
            return process.State.ToString();
        }

        if (_lifecycleService.IsWaitingToRestart(instanceId))
        {
            return "Restarting";
        }

        return _lifecycleService.IsRunning(instanceId) ? "Online" : "Offline";
    }
}

