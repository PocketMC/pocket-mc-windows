using System;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Instances;



namespace PocketMC.Application.Interfaces
{
    public interface IServerLifecycleService
    {
        event Action<Guid, ServerState>? OnInstanceStateChanged;
        event Action<Guid, int>? OnRestartCountdownTick;

        Task StartAsync(InstanceMetadata meta);
        Task StopAsync(Guid instanceId);
        void Kill(Guid instanceId);
        void KillAll();

        bool IsRunning(Guid instanceId);
        bool IsWaitingToRestart(Guid instanceId);
        void AbortRestartDelay(Guid instanceId);
        Task RestartAsync(Guid instanceId);

        IServerProcess? GetProcess(Guid instanceId);
        DateTime? GetSessionStartTime(Guid instanceId);
        Task ReleaseInstanceAsync(Guid instanceId);
    }
}

