using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances;

public class ServerLifecycleService : IServerLifecycleService
{
    private readonly ServerProcessManager _processManager;
    private readonly InstanceRegistry _registry;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ServerLifecycleService> _logger;
    private readonly PocketMC.Desktop.Features.Shell.ApplicationState _appState;
    private string _appRootPath => _appState.GetRequiredAppRootPath();

    private readonly ConcurrentDictionary<Guid, int> _consecutiveRestarts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastStartTime = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _restartCancellations = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _sessionStartTimes = new();

    public event Action<Guid, ServerState>? OnInstanceStateChanged;
    public event Action<Guid, int>? OnRestartCountdownTick;

    public ServerLifecycleService(
        ServerProcessManager processManager,
        InstanceRegistry registry,
        INotificationService notificationService,
        ILogger<ServerLifecycleService> logger,
        PocketMC.Desktop.Features.Shell.ApplicationState appState)
    {
        _processManager = processManager;
        _registry = registry;
        _notificationService = notificationService;
        _logger = logger;
        _appState = appState;

        _processManager.OnInstanceStateChanged += (id, state) => OnInstanceStateChanged?.Invoke(id, state);
        _processManager.OnServerCrashed += async (id, log) => await HandleServerCrashAsync(id);
    }

    public async Task StartAsync(InstanceMetadata meta)
    {
        if (_lastStartTime.TryGetValue(meta.Id, out var last) && (DateTime.UtcNow - last).TotalMinutes > 10)
            _consecutiveRestarts[meta.Id] = 0;

        _lastStartTime[meta.Id] = DateTime.UtcNow;
        _sessionStartTimes[meta.Id] = DateTime.UtcNow;
        await _processManager.StartProcessAsync(meta, _appRootPath);
    }

    public async Task StopAsync(Guid instanceId)
    {
        AbortRestartDelay(instanceId);
        await _processManager.StopProcessAsync(instanceId);
    }

    public void Kill(Guid instanceId)
    {
        AbortRestartDelay(instanceId);
        _processManager.KillProcess(instanceId);
    }

    public void KillAll()
    {
        foreach (var cts in _restartCancellations.Values) cts.Cancel();
        _processManager.KillAll();
    }

    public bool IsRunning(Guid instanceId) => _processManager.IsRunning(instanceId);
    public bool IsWaitingToRestart(Guid instanceId) => _restartCancellations.ContainsKey(instanceId);

    public void AbortRestartDelay(Guid instanceId)
    {
        if (_restartCancellations.TryRemove(instanceId, out var cts))
        {
            cts.Cancel();
            OnInstanceStateChanged?.Invoke(instanceId, ServerState.Crashed);
        }
    }

    public ServerProcess? GetProcess(Guid instanceId) => _processManager.GetProcess(instanceId);

    /// <summary>
    /// Gets the UTC timestamp of when the current/last session started, or null if never started.
    /// </summary>
    public DateTime? GetSessionStartTime(Guid instanceId) =>
        _sessionStartTimes.TryGetValue(instanceId, out var time) ? time : null;

    public async Task RestartAsync(Guid instanceId)
    {
        var meta = _registry.GetById(instanceId);
        if (meta == null) return;

        await StopAsync(instanceId);
        // Wait a small buffer for OS to release locks
        await Task.Delay(800);
        await StartAsync(meta);
    }

    private async Task HandleServerCrashAsync(Guid instanceId)
    {
        var meta = _registry.GetById(instanceId);
        if (meta == null || !meta.EnableAutoRestart) return;

        int attempts = _consecutiveRestarts.GetOrAdd(instanceId, 0);
        if (attempts >= meta.MaxAutoRestarts)
        {
            _notificationService.ShowInformation("Restart Limit Reached", $"Server '{meta.Name}' stopped after {attempts} failed restarts.");
            return;
        }

        var cts = new CancellationTokenSource();
        _restartCancellations[instanceId] = cts;
        var delay = (int)Math.Min(meta.AutoRestartDelaySeconds * Math.Pow(2, attempts), 300);

        try
        {
            for (int i = delay; i > 0; i--)
            {
                if (cts.Token.IsCancellationRequested) return;
                OnRestartCountdownTick?.Invoke(instanceId, i);
                await Task.Delay(1000, cts.Token);
            }
            _consecutiveRestarts[instanceId] = attempts + 1;
            await StartAsync(meta);
        }
        catch (TaskCanceledException) { }
        finally { _restartCancellations.TryRemove(instanceId, out _); }
    }
}
