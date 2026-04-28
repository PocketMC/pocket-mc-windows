using PocketMC.Desktop.Features.Instances.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure;
using PocketMC.Desktop.Features.Players.Services;

namespace PocketMC.Desktop.Features.Instances.Services;

/// <summary>
/// Low-level process tracker that manages concrete OS process interactions.
/// </summary>
public class ServerProcessManager
{
    public static int CalculateRestartDelaySeconds(int baseDelay, int attempt)
    {
        return (int)Math.Min(baseDelay * Math.Pow(2, attempt), 300);
    }

    private readonly JobObject _jobObject;
    private readonly InstanceManager _instanceManager;
    private readonly InstanceRegistry _registry;
    private readonly ServerLaunchConfigurator _launchConfigurator;
    private readonly PlayerListParser _playerListParser;
    private readonly ILogger<ServerProcessManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<Guid, ServerProcess> _activeProcesses = new();
    private readonly ConcurrentDictionary<Guid, ServerProcess> _historicalProcesses = new();

    public ServerProcessManager(
        JobObject jobObject,
        InstanceManager instanceManager,
        InstanceRegistry registry,
        ServerLaunchConfigurator launchConfigurator,
        PlayerListParser playerListParser,
        ILogger<ServerProcessManager> logger,
        ILoggerFactory loggerFactory)
    {
        _jobObject = jobObject;
        _instanceManager = instanceManager;
        _registry = registry;
        _launchConfigurator = launchConfigurator;
        _playerListParser = playerListParser;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public event Action<Guid, ServerState>? OnInstanceStateChanged;
    public event Action<Guid, string>? OnServerCrashed;

    public ConcurrentDictionary<Guid, ServerProcess> ActiveProcesses => _activeProcesses;

    public async Task<ServerProcess> StartProcessAsync(InstanceMetadata meta, string appRootPath)
    {
        if (_activeProcesses.ContainsKey(meta.Id))
            throw new InvalidOperationException($"Server '{meta.Name}' is already running.");

        var instancePath = _registry.GetPath(meta.Id)
            ?? throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");

        var serverProcess = new ServerProcess(
            meta.Id,
            _jobObject,
            _launchConfigurator,
            _playerListParser,
            _loggerFactory.CreateLogger<ServerProcess>());

        serverProcess.OnStateChanged += state =>
        {
            OnInstanceStateChanged?.Invoke(meta.Id, state);
            if (state == ServerState.Stopped || state == ServerState.Crashed)
                _activeProcesses.TryRemove(meta.Id, out _);
        };

        serverProcess.OnServerCrashed += crashLog => OnServerCrashed?.Invoke(meta.Id, crashLog);

        _activeProcesses[meta.Id] = serverProcess;
        _historicalProcesses[meta.Id] = serverProcess;

        try
        {
            await serverProcess.StartAsync(meta, instancePath, appRootPath);
        }
        catch
        {
            _activeProcesses.TryRemove(meta.Id, out _);
            throw;
        }

        return serverProcess;
    }

    public async Task StopProcessAsync(Guid instanceId)
    {
        if (_activeProcesses.TryGetValue(instanceId, out var process))
            await process.StopAsync();
    }

    public void KillProcess(Guid instanceId)
    {
        if (_activeProcesses.TryGetValue(instanceId, out var process))
            process.Kill();
    }

    public bool IsRunning(Guid instanceId)
    {
        return _activeProcesses.TryGetValue(instanceId, out var process) &&
               process.State != ServerState.Stopped &&
               process.State != ServerState.Crashed;
    }

    public ServerProcess? GetProcess(Guid instanceId)
    {
        if (_activeProcesses.TryGetValue(instanceId, out var process)) return process;
        _historicalProcesses.TryGetValue(instanceId, out var historical);
        return historical;
    }

    public void ReleaseInstance(Guid instanceId)
    {
        if (_activeProcesses.TryRemove(instanceId, out var active))
        {
            active.Dispose();
        }
        if (_historicalProcesses.TryRemove(instanceId, out var historical))
        {
            historical.Dispose();
        }
    }

    public void KillAll()
    {
        foreach (var kvp in _activeProcesses)
        {
            try { kvp.Value.Kill(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill server instance {InstanceId}.", kvp.Key); }
        }
        _activeProcesses.Clear();
    }
}
