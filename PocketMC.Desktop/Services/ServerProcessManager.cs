using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services;

/// <summary>
/// Global singleton managing all active Minecraft server processes.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public class ServerProcessManager
{
    private readonly JobObject _jobObject;
    private readonly InstanceManager _instanceManager;
    private readonly ILogger<ServerProcessManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<Guid, ServerProcess> _activeProcesses = new();
    private readonly ConcurrentDictionary<Guid, ServerProcess> _historicalProcesses = new();

    // Auto-Restart Tracking State
    private readonly ConcurrentDictionary<Guid, int> _consecutiveRestarts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastStartTime = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _restartCancellations = new();

    public ServerProcessManager(
        JobObject jobObject,
        InstanceManager instanceManager,
        ILogger<ServerProcessManager> logger,
        ILoggerFactory loggerFactory)
    {
        _jobObject = jobObject;
        _instanceManager = instanceManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Fires when any instance changes state (started, stopped, crashed).
    /// </summary>
    public event Action<Guid, ServerState>? OnInstanceStateChanged;

    /// <summary>
    /// Fires every second while a crashed server is waiting to auto-restart.
    /// </summary>
    public event Action<Guid, int>? OnRestartCountdownTick;

    /// <summary>
    /// Gets the collection of currently active server processes.
    /// </summary>
    public ConcurrentDictionary<Guid, ServerProcess> ActiveProcesses => _activeProcesses;

    /// <summary>
    /// Starts a server process for the given instance.
    /// Throws if already running, or if java/server.jar missing.
    /// </summary>
    public ServerProcess StartProcess(InstanceMetadata meta, string appRootPath)
    {
        if (_activeProcesses.ContainsKey(meta.Id))
        {
            throw new InvalidOperationException($"Server '{meta.Name}' is already running.");
        }

        // Reset consecutive restarts if it's been running stably for >10 mins
        if (_lastStartTime.TryGetValue(meta.Id, out var lastStart) &&
            (DateTime.UtcNow - lastStart).TotalMinutes > 10)
        {
            _consecutiveRestarts[meta.Id] = 0;
        }

        _lastStartTime[meta.Id] = DateTime.UtcNow;
        var instancePath = _instanceManager.GetInstancePath(meta.Id);
        if (string.IsNullOrEmpty(instancePath))
        {
            throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");
        }

        var serverProcess = new ServerProcess(
            meta.Id,
            _jobObject,
            _loggerFactory.CreateLogger<ServerProcess>());

        serverProcess.OnStateChanged += state =>
        {
            OnInstanceStateChanged?.Invoke(meta.Id, state);

            if (state == ServerState.Stopped || state == ServerState.Crashed)
            {
                _activeProcesses.TryRemove(meta.Id, out _);
            }
        };

        serverProcess.OnServerCrashed += async crashLog =>
        {
            _logger.LogWarning("Server {ServerName} crashed. Crash context length: {CrashLength}", meta.Name, crashLog.Length);
            await HandleServerCrashAsync(meta, appRootPath);
        };

        serverProcess.Start(meta, instancePath, appRootPath);
        _activeProcesses[meta.Id] = serverProcess;
        _historicalProcesses[meta.Id] = serverProcess;

        return serverProcess;
    }

    private async Task HandleServerCrashAsync(InstanceMetadata meta, string appRootPath)
    {
        if (!meta.EnableAutoRestart)
        {
            return;
        }

        int attempts = _consecutiveRestarts.GetOrAdd(meta.Id, 0);

        if (attempts >= meta.MaxAutoRestarts)
        {
            _logger.LogWarning(
                "Server {ServerName} reached the max auto-restart limit after {Attempts} consecutive crashes.",
                meta.Name,
                attempts);

            new ToastContentBuilder()
                .AddText("PocketMC Server Crashed")
                .AddText($"Server '{meta.Name}' has crashed consecutively {attempts} times and hit the max auto-restart limit.")
                .Show();
            return;
        }

        var cts = new CancellationTokenSource();
        _restartCancellations[meta.Id] = cts;

        var backoffSeconds = CalculateRestartDelaySeconds(meta.AutoRestartDelaySeconds, attempts);
        _logger.LogInformation(
            "Scheduling auto-restart for {ServerName} in {DelaySeconds}s after crash attempt {Attempt}.",
            meta.Name,
            backoffSeconds,
            attempts + 1);

        try
        {
            for (int i = backoffSeconds; i > 0; i--)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    break;
                }

                OnRestartCountdownTick?.Invoke(meta.Id, i);
                await Task.Delay(1000, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Auto-restart for server {ServerName} was cancelled.", meta.Name);
        }
        finally
        {
            _restartCancellations.TryRemove(meta.Id, out _);
        }

        if (!cts.IsCancellationRequested)
        {
            _consecutiveRestarts[meta.Id] = attempts + 1;
            StartProcess(meta, appRootPath);
        }
    }

    internal static int CalculateRestartDelaySeconds(int baseDelaySeconds, int attempts)
    {
        var safeBaseDelay = Math.Max(1, baseDelaySeconds);
        var scaledDelay = safeBaseDelay * Math.Pow(2, attempts);
        return (int)Math.Min(scaledDelay, 300);
    }

    public bool IsWaitingToRestart(Guid instanceId)
    {
        return _restartCancellations.ContainsKey(instanceId);
    }

    public void AbortRestartDelay(Guid instanceId)
    {
        if (_restartCancellations.TryGetValue(instanceId, out var cts))
        {
            cts.Cancel();
            OnInstanceStateChanged?.Invoke(instanceId, ServerState.Crashed);
        }
    }

    /// <summary>
    /// Gracefully stops a running server by sending /stop and waiting.
    /// </summary>
    public async Task StopProcessAsync(Guid instanceId)
    {
        AbortRestartDelay(instanceId);
        if (_activeProcesses.TryGetValue(instanceId, out var process))
        {
            await process.StopAsync();
        }
    }

    /// <summary>
    /// Force kills a running server immediately.
    /// </summary>
    public void KillProcess(Guid instanceId)
    {
        AbortRestartDelay(instanceId);
        if (_activeProcesses.TryGetValue(instanceId, out var process))
        {
            process.Kill();
        }
    }

    /// <summary>
    /// Returns whether a specific instance is currently running.
    /// </summary>
    public bool IsRunning(Guid instanceId)
    {
        return _activeProcesses.ContainsKey(instanceId) &&
               _activeProcesses[instanceId].State != ServerState.Stopped &&
               _activeProcesses[instanceId].State != ServerState.Crashed;
    }

    /// <summary>
    /// Gets the ServerProcess for a running instance, or null.
    /// </summary>
    public ServerProcess? GetProcess(Guid instanceId)
    {
        if (_activeProcesses.TryGetValue(instanceId, out var process))
        {
            return process;
        }

        _historicalProcesses.TryGetValue(instanceId, out var historical);
        return historical;
    }

    /// <summary>
    /// Kills all running processes. Called on application shutdown.
    /// </summary>
    public void KillAll()
    {
        foreach (var cts in _restartCancellations.Values)
        {
            cts.Cancel();
        }

        _restartCancellations.Clear();

        foreach (var kvp in _activeProcesses)
        {
            try
            {
                kvp.Value.Kill();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill server instance {InstanceId} during shutdown.", kvp.Key);
            }
        }

        _activeProcesses.Clear();
    }
}
