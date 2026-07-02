using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Features.Tunnel;
using PocketMC.Desktop.Infrastructure.Power;

namespace PocketMC.Desktop.Infrastructure
{
    public class ApplicationLifecycleService : IApplicationLifecycleService
    {
        private readonly IServerLifecycleService _serverLifecycle;
        private readonly ServerProcessManager _serverProcessManager;
        private readonly PlayitAgentService _playitAgentService;
        private readonly IDiscordRpcService _discordRpcService;
        private readonly SleepPreventionService _sleepPreventionService;
        private readonly ILogger<ApplicationLifecycleService> _logger;

        public ApplicationLifecycleService(
            IServerLifecycleService serverLifecycle,
            ServerProcessManager serverProcessManager,
            PlayitAgentService playitAgentService,
            IDiscordRpcService discordRpcService,
            SleepPreventionService sleepPreventionService,
            ILogger<ApplicationLifecycleService> logger)
        {
            _serverLifecycle = serverLifecycle;
            _serverProcessManager = serverProcessManager;
            _playitAgentService = playitAgentService;
            _discordRpcService = discordRpcService;
            _sleepPreventionService = sleepPreventionService;
            _logger = logger;
        }

        public async Task GracefulShutdownAsync()
        {
            try
            {
                var activeIds = _serverProcessManager.ActiveProcesses.Keys.ToList();
                if (activeIds.Count > 0)
                {
                    _logger.LogInformation("Gracefully stopping {Count} servers.", activeIds.Count);
                    var stopTasks = activeIds.Select(id => _serverLifecycle.StopAsync(id));
                    await Task.WhenAll(stopTasks);
                }

                if (_playitAgentService.IsRunning)
                {
                    _logger.LogInformation("Disconnecting Playit.gg tunnels.");
                    _playitAgentService.Stop();
                }

                _discordRpcService.Shutdown();
            }
            finally
            {
                _sleepPreventionService.AllowSleep();
            }
        }
    }
}
