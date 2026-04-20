using PocketMC.Desktop.Features.Instances.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Shell.Interfaces;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances.Services;

public class ServerLifecycleService : IServerLifecycleService, IDisposable
{
    private readonly ServerProcessManager _processManager;
    private readonly InstanceRegistry _registry;
    private readonly PortPreflightService _portPreflightService;
    private readonly PortProbeService _portProbeService;
    private readonly PortLeaseRegistry _portLeaseRegistry;
    private readonly PortRecoveryService _portRecoveryService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ServerLifecycleService> _logger;
    private readonly PocketMC.Desktop.Features.Shell.ApplicationState _appState;
    private readonly GeyserProvisioningService _geyserProvisioningService;
    private string _appRootPath => _appState.GetRequiredAppRootPath();

    private readonly ConcurrentDictionary<Guid, int> _consecutiveRestarts = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastStartTime = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _restartCancellations = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _sessionStartTimes = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _startLocks = new();
    private readonly SemaphoreSlim _portReservationLock = new(1, 1);

    public event Action<Guid, ServerState>? OnInstanceStateChanged;
    public event Action<Guid, int>? OnRestartCountdownTick;

    public ServerLifecycleService(
        ServerProcessManager processManager,
        InstanceRegistry registry,
        PortPreflightService portPreflightService,
        PortProbeService portProbeService,
        PortLeaseRegistry portLeaseRegistry,
        PortRecoveryService portRecoveryService,
        INotificationService notificationService,
        ILogger<ServerLifecycleService> logger,
        PocketMC.Desktop.Features.Shell.ApplicationState appState,
        GeyserProvisioningService geyserProvisioningService)
    {
        _processManager = processManager;
        _registry = registry;
        _portPreflightService = portPreflightService;
        _portProbeService = portProbeService;
        _portLeaseRegistry = portLeaseRegistry;
        _portRecoveryService = portRecoveryService;
        _notificationService = notificationService;
        _logger = logger;
        _appState = appState;
        _geyserProvisioningService = geyserProvisioningService;

        _processManager.OnInstanceStateChanged += HandleInstanceStateChanged;
        _processManager.OnServerCrashed += HandleProcessManagerServerCrashed;
    }

    public async Task StartAsync(InstanceMetadata meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        SemaphoreSlim startLock = _startLocks.GetOrAdd(meta.Id, static _ => new SemaphoreSlim(1, 1));
        bool leasesAcquired = false;
        int recoveryAttempt = 0;

        await startLock.WaitAsync();
        try
        {
            if (_processManager.IsRunning(meta.Id))
                throw new InvalidOperationException($"Server '{meta.Name}' is already running.");

            string instancePath = _registry.GetPath(meta.Id)
                ?? throw new DirectoryNotFoundException($"Could not locate directory for instance {meta.Name}.");

            if (meta.HasGeyser)
            {
                _geyserProvisioningService.PatchGeyserConfigPort(instancePath, meta.GeyserBedrockPort ?? 19132);
            }

            while (true)
            {
                try
                {
                    await _portReservationLock.WaitAsync();
                    try
                    {
                        var preflightResult = _portPreflightService.Check(meta, instancePath);
                        if (!preflightResult.IsSuccessful)
                            throw CreatePortReliabilityException(new[] { preflightResult }, recoveryAttempt);

                        IReadOnlyList<PortCheckRequest> portRequests = _portPreflightService.BuildRequests(meta, instancePath);
                        PortCheckResult[] failedProbeResults = _portProbeService.ProbeMany(portRequests)
                            .Where(result => !result.IsSuccessful)
                            .ToArray();

                        if (failedProbeResults.Length > 0)
                            throw CreatePortReliabilityException(failedProbeResults, recoveryAttempt);

                        PortCheckResult? leaseFailure = TryReservePortLeases(meta, instancePath, portRequests);
                        if (leaseFailure != null)
                            throw CreatePortReliabilityException(new[] { leaseFailure }, recoveryAttempt);

                        leasesAcquired = true;
                    }
                    finally
                    {
                        _portReservationLock.Release();
                    }

                    break;
                }
                catch (PortReliabilityException ex) when (TryGetRecoveryDelay(ex.PrimaryResult, recoveryAttempt, out TimeSpan retryDelay))
                {
                    if (leasesAcquired)
                    {
                        CleanupInstanceNetworking(meta.Id);
                        leasesAcquired = false;
                    }

                    LogPortRecoveryAttempt(meta, ex.PrimaryResult, retryDelay, recoveryAttempt);
                    recoveryAttempt++;
                    await Task.Delay(retryDelay);
                }
            }

            if (_lastStartTime.TryGetValue(meta.Id, out var last) && (DateTime.UtcNow - last).TotalMinutes > 10)
                _consecutiveRestarts[meta.Id] = 0;

            _lastStartTime[meta.Id] = DateTime.UtcNow;
            _sessionStartTimes[meta.Id] = DateTime.UtcNow;
            await _processManager.StartProcessAsync(meta, _appRootPath);
        }
        catch (PortReliabilityException ex)
        {
            if (leasesAcquired)
            {
                CleanupInstanceNetworking(meta.Id);
                _sessionStartTimes.TryRemove(meta.Id, out _);
            }

            LogPortReliabilityFailure(meta, ex.Results);
            throw;
        }
        catch
        {
            if (leasesAcquired)
            {
                CleanupInstanceNetworking(meta.Id);
                _sessionStartTimes.TryRemove(meta.Id, out _);
            }

            throw;
        }
        finally
        {
            startLock.Release();
        }
    }

    public async Task StopAsync(Guid instanceId)
    {
        AbortRestartDelay(instanceId);
        await _processManager.StopProcessAsync(instanceId);
        CleanupInstanceNetworking(instanceId);
    }

    public void Kill(Guid instanceId)
    {
        AbortRestartDelay(instanceId);
        _processManager.KillProcess(instanceId);
        CleanupInstanceNetworking(instanceId);
    }

    public void KillAll()
    {
        Guid[] instanceIds = _processManager.ActiveProcesses.Keys
            .Concat(_portLeaseRegistry.GetAllLeases()
                .Where(lease => lease.InstanceId.HasValue)
                .Select(lease => lease.InstanceId!.Value))
            .Distinct()
            .ToArray();

        CancelAllRestartDelays();
        _processManager.KillAll();

        foreach (Guid instanceId in instanceIds)
        {
            CleanupInstanceNetworking(instanceId);
        }
    }

    public bool IsRunning(Guid instanceId) => _processManager.IsRunning(instanceId);
    public bool IsWaitingToRestart(Guid instanceId) => _restartCancellations.ContainsKey(instanceId);

    public void AbortRestartDelay(Guid instanceId)
    {
        CancelRestartDelay(instanceId, notifyStateChange: true);
    }

    public async Task ReleaseInstanceAsync(Guid instanceId)
    {
        if (_processManager.IsRunning(instanceId))
        {
            await StopAsync(instanceId);
        }

        CancelRestartDelay(instanceId, notifyStateChange: false);
        _processManager.ReleaseInstance(instanceId);
        CleanupInstanceNetworking(instanceId);
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

    /// <summary>
    /// Releases lifecycle event subscriptions and any residual networking state.
    /// </summary>
    public void Dispose()
    {
        _processManager.OnInstanceStateChanged -= HandleInstanceStateChanged;
        _processManager.OnServerCrashed -= HandleProcessManagerServerCrashed;

        CancelAllRestartDelays();
        ReleaseResidualNetworkingState();

        foreach (SemaphoreSlim startLock in _startLocks.Values)
        {
            startLock.Dispose();
        }

        _portReservationLock.Dispose();
        _startLocks.Clear();
        _sessionStartTimes.Clear();
        _lastStartTime.Clear();
        _consecutiveRestarts.Clear();
    }

    private async void HandleProcessManagerServerCrashed(Guid instanceId, string _)
    {
        await HandleServerCrashAsync(instanceId);
    }

    private async Task HandleServerCrashAsync(Guid instanceId)
    {
        CleanupInstanceNetworking(instanceId);

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
        catch (InvalidOperationException ex) when (_processManager.IsRunning(instanceId))
        {
            _logger.LogDebug(ex, "Auto-restart skipped for '{ServerName}' because the server is already running.", meta.Name);
        }
        catch (PortReliabilityException ex)
        {
            _notificationService.ShowInformation("Restart Blocked", $"Server '{meta.Name}' could not restart.\n\n{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-restart failed for server '{ServerName}'.", meta.Name);
            _notificationService.ShowInformation("Restart Failed", $"Server '{meta.Name}' could not restart automatically.");
        }
        finally
        {
            _restartCancellations.TryRemove(instanceId, out _);
            cts.Dispose();
        }
    }

    private void HandleInstanceStateChanged(Guid instanceId, ServerState state)
    {
        if (state == ServerState.Stopped || state == ServerState.Crashed)
        {
            CleanupInstanceNetworking(instanceId);
        }

        OnInstanceStateChanged?.Invoke(instanceId, state);
    }

    private PortCheckResult? TryReservePortLeases(InstanceMetadata meta, string instancePath, IReadOnlyList<PortCheckRequest> portRequests)
    {
        var reservedRequests = new List<PortCheckRequest>(portRequests.Count);

        foreach (PortCheckRequest request in portRequests)
        {
            var lease = new PortLease(
                request.Port,
                request.Protocol,
                request.IpMode,
                meta.Id,
                meta.Name,
                instancePath,
                request.BindAddress);

            if (_portLeaseRegistry.TryReserve(lease, out PortLease? conflictingLease))
            {
                reservedRequests.Add(request);
                continue;
            }

            ReleaseReservedPortLeases(meta.Id, reservedRequests);

            var conflict = new PortConflictInfo(
                PortFailureCode.InUseByPocketMcInstance,
                request.Port,
                request.Protocol,
                request.IpMode,
                request.BindAddress,
                existingLease: conflictingLease,
                details: conflictingLease?.InstanceName == null
                    ? "Another PocketMC startup already reserved the required port binding."
                    : $"Instance '{conflictingLease.InstanceName}' already reserved the required port binding.");

            var recommendation = new PortRecoveryRecommendation(
                PortFailureCode.InUseByPocketMcInstance,
                "Release the competing PocketMC lease",
                conflictingLease?.InstanceName == null
                    ? "Wait for the other PocketMC startup to finish or change this instance to a different port."
                    : $"Stop '{conflictingLease.InstanceName}' or change this instance to a different port.",
                suggestedProtocol: request.Protocol,
                suggestedIpMode: request.IpMode,
                canAutoApply: false,
                requiresUserAction: true);

            return new PortCheckResult(
                request,
                isSuccessful: false,
                canBindLocally: false,
                failureCode: PortFailureCode.InUseByPocketMcInstance,
                failureMessage: $"Port {request.Port} is already reserved by another PocketMC instance.",
                lease: null,
                conflicts: new[] { conflict },
                recommendations: new[] { recommendation });
        }

        return null;
    }

    private void ReleaseReservedPortLeases(Guid instanceId, IReadOnlyList<PortCheckRequest> reservedRequests)
    {
        foreach (PortCheckRequest reserved in reservedRequests)
        {
            _portLeaseRegistry.Release(
                instanceId,
                reserved.Port,
                reserved.Protocol,
                reserved.IpMode,
                reserved.BindAddress);
        }
    }

    private void CleanupInstanceNetworking(Guid instanceId)
    {
        int released = _portLeaseRegistry.ReleaseInstance(instanceId);
        _appState.ClearTunnelAddress(instanceId);

        if (released > 0)
        {
            _logger.LogDebug("Released {LeaseCount} port lease(s) for instance {InstanceId}.", released, instanceId);
        }
    }

    private bool CancelRestartDelay(Guid instanceId, bool notifyStateChange)
    {
        if (!_restartCancellations.TryRemove(instanceId, out CancellationTokenSource? cts))
        {
            return false;
        }

        cts.Cancel();
        CleanupInstanceNetworking(instanceId);
        _logger.LogDebug("Cancelled the pending auto-restart delay for instance {InstanceId}.", instanceId);

        if (notifyStateChange)
        {
            OnInstanceStateChanged?.Invoke(instanceId, ServerState.Crashed);
        }

        return true;
    }

    private void CancelAllRestartDelays()
    {
        Guid[] waitingInstanceIds = _restartCancellations.Keys.ToArray();
        foreach (Guid instanceId in waitingInstanceIds)
        {
            CancelRestartDelay(instanceId, notifyStateChange: false);
        }
    }

    private void ReleaseResidualNetworkingState()
    {
        Guid[] instanceIds = _portLeaseRegistry.GetAllLeases()
            .Where(lease => lease.InstanceId.HasValue)
            .Select(lease => lease.InstanceId!.Value)
            .Concat(_sessionStartTimes.Keys)
            .Distinct()
            .ToArray();

        foreach (Guid instanceId in instanceIds)
        {
            CleanupInstanceNetworking(instanceId);
        }
    }

    private void LogPortReliabilityFailure(InstanceMetadata meta, IReadOnlyList<PortCheckResult> results)
    {
        foreach (PortCheckResult result in results.Where(x => !x.IsSuccessful))
        {
            _logger.LogWarning(
                "Port startup validation failed for '{ServerName}' ({InstanceId}). Code={FailureCode}, Binding={Binding}, Engine={Engine}, Port={Port}, Protocol={Protocol}, IpMode={IpMode}, Message={Message}",
                meta.Name,
                meta.Id,
                result.FailureCode,
                result.Request.BindingRole,
                result.Request.Engine,
                result.Request.Port,
                result.Request.Protocol,
                result.Request.IpMode,
                result.FailureMessage);
        }
    }

    private PortReliabilityException CreatePortReliabilityException(IEnumerable<PortCheckResult> results, int attemptNumber)
    {
        PortCheckResult[] enhancedResults = results
            .Select(result => AddRecoveryRecommendation(result, attemptNumber))
            .ToArray();

        return new PortReliabilityException(enhancedResults);
    }

    private PortCheckResult AddRecoveryRecommendation(PortCheckResult result, int attemptNumber)
    {
        PortRecoveryRecommendation recommendation = _portRecoveryService.Recommend(
            result,
            attemptNumber,
            allowAutoPortSwitch: false);

        return new PortCheckResult(
            result.Request,
            result.IsSuccessful,
            result.CanBindLocally,
            result.FailureCode,
            result.FailureMessage,
            result.Lease,
            result.Conflicts,
            new[] { recommendation }.Concat(result.Recommendations),
            result.CheckedAtUtc);
    }

    private bool TryGetRecoveryDelay(PortCheckResult result, int attemptNumber, out TimeSpan retryDelay)
    {
        PortRecoveryRecommendation? recommendation = result.Recommendations.FirstOrDefault(x =>
            x.Action == PortRecoveryAction.WaitWithBackoff &&
            x.AttemptNumber == attemptNumber);

        if (recommendation?.RetryDelay.HasValue == true)
        {
            retryDelay = recommendation.RetryDelay.Value;
            return true;
        }

        retryDelay = TimeSpan.Zero;
        return false;
    }

    private void LogPortRecoveryAttempt(
        InstanceMetadata meta,
        PortCheckResult result,
        TimeSpan retryDelay,
        int attemptNumber)
    {
        _logger.LogInformation(
            "Port startup validation for '{ServerName}' ({InstanceId}) failed with {FailureCode} on port {Port}. Retrying after {DelaySeconds}s. Attempt={AttemptNumber}.",
            meta.Name,
            meta.Id,
            result.FailureCode,
            result.Request.Port,
            retryDelay.TotalSeconds,
            attemptNumber + 1);
    }
}
