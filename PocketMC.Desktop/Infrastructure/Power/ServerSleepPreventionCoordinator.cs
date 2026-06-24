using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Infrastructure.Power;

public sealed class ServerSleepPreventionCoordinator : IDisposable
{
    private readonly ServerProcessManager _serverProcessManager;
    private readonly ApplicationState _applicationState;
    private readonly SleepPreventionService _sleepPreventionService;
    private readonly ILogger<ServerSleepPreventionCoordinator> _logger;
    private readonly object _gate = new();
    private bool _disposed;

    public ServerSleepPreventionCoordinator(
        ServerProcessManager serverProcessManager,
        ApplicationState applicationState,
        SleepPreventionService sleepPreventionService,
        ILogger<ServerSleepPreventionCoordinator> logger)
    {
        _serverProcessManager = serverProcessManager;
        _applicationState = applicationState;
        _sleepPreventionService = sleepPreventionService;
        _logger = logger;
        _serverProcessManager.OnInstanceStateChanged += OnInstanceStateChanged;
    }

    public void Refresh()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            bool shouldPreventSleep =
                _applicationState.Settings.KeepComputerAwakeWhileServersRunning &&
                _serverProcessManager.ActiveProcesses.Count > 0;

            try
            {
                if (shouldPreventSleep)
                {
                    _sleepPreventionService.PreventSleep();
                }
                else
                {
                    _sleepPreventionService.AllowSleep();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh server sleep prevention state.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _serverProcessManager.OnInstanceStateChanged -= OnInstanceStateChanged;
            _sleepPreventionService.AllowSleep();
        }
    }

    private void OnInstanceStateChanged(Guid instanceId, ServerState state)
    {
        _ = Task.Run(Refresh);
    }
}
