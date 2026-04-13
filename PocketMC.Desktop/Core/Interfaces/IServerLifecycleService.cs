using System;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Instances;

namespace PocketMC.Desktop.Core.Interfaces
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

        ServerProcess? GetProcess(Guid instanceId);
        DateTime? GetSessionStartTime(Guid instanceId);
    }
}
