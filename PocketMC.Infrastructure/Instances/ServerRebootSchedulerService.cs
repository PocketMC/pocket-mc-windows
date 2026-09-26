using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances;

/// <summary>
/// Background scheduler that monitors running Minecraft server instances and executes
/// scheduled reboots according to their configured daily time or recurring interval.
/// </summary>
public class ServerRebootSchedulerService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly ApplicationState _applicationState;
    private readonly InstanceRegistry _registry;
    private readonly InstanceManager _instanceManager;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ServerRebootSchedulerService> _logger;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _inFlightReboots = new();
    private int _isProcessing;
    private bool _isDisposed;

    public ServerRebootSchedulerService(
        ApplicationState applicationState,
        InstanceRegistry registry,
        InstanceManager instanceManager,
        IServerLifecycleService lifecycleService,
        INotificationService notificationService,
        ILogger<ServerRebootSchedulerService> logger)
    {
        _applicationState = applicationState;
        _registry = registry;
        _instanceManager = instanceManager;
        _lifecycleService = lifecycleService;
        _notificationService = notificationService;
        _logger = logger;

        _timer = new System.Timers.Timer(30_000); // Check every 30 seconds
        _timer.Elapsed += OnTimerTick;
        _timer.AutoReset = true;

        _lifecycleService.OnInstanceStateChanged += HandleInstanceStateChanged;
    }

    public void Start()
    {
        if (_isDisposed) return;
        _timer.Start();
        _logger.LogInformation("Server reboot scheduler started.");
    }

    public void Stop()
    {
        _timer.Stop();
        AbortAllPendingReboots();
        _logger.LogInformation("Server reboot scheduler stopped.");
    }

    public bool IsRebootPending(Guid instanceId) => _inFlightReboots.ContainsKey(instanceId);

    public void AbortScheduledReboot(Guid instanceId)
    {
        if (_inFlightReboots.TryRemove(instanceId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
            finally
            {
                cts.Dispose();
            }

            _logger.LogInformation("Scheduled reboot aborted for instance {InstanceId}.", instanceId);
        }
    }

    private void AbortAllPendingReboots()
    {
        foreach (var kvp in _inFlightReboots.ToArray())
        {
            AbortScheduledReboot(kvp.Key);
        }
    }

    private void HandleInstanceStateChanged(Guid instanceId, ServerState state)
    {
        if (state is ServerState.Stopping or ServerState.Stopped or ServerState.Crashed)
        {
            AbortScheduledReboot(instanceId);
        }
    }

    private async void OnTimerTick(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!_applicationState.IsConfigured)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (var meta in _registry.GetAll())
            {
                if (_inFlightReboots.ContainsKey(meta.Id))
                {
                    continue;
                }

                if (IsDue(meta, nowUtc))
                {
                    _ = Task.Run(() => ExecuteScheduledRebootAsync(meta));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during scheduled reboot evaluation loop.");
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessing, 0);
        }
    }

    public bool IsDue(InstanceMetadata meta, DateTime nowUtc)
    {
        if (meta == null || !meta.EnableScheduledReboot)
        {
            return false;
        }

        if (!_lifecycleService.IsRunning(meta.Id))
        {
            return false;
        }

        if (_lifecycleService.IsWaitingToRestart(meta.Id))
        {
            return false;
        }

        var process = _lifecycleService.GetProcess(meta.Id);
        if (process == null || process.State != ServerState.Online)
        {
            return false;
        }

        var sessionStart = _lifecycleService.GetSessionStartTime(meta.Id);
        var minUptime = TimeSpan.FromMinutes(1);
        if (sessionStart.HasValue && (nowUtc - sessionStart.Value) < minUptime)
        {
            return false;
        }

        if (string.Equals(meta.ScheduledRebootMode, "Interval", StringComparison.OrdinalIgnoreCase))
        {
            int intervalHours = meta.ScheduledRebootIntervalHours > 0 ? meta.ScheduledRebootIntervalHours : 24;
            DateTime referenceTime = meta.LastScheduledRebootTime ?? sessionStart ?? nowUtc;
            DateTime dueTime = referenceTime.AddHours(intervalHours);
            return nowUtc >= dueTime;
        }
        else
        {
            if (!TryParseTimeOfDay(meta.ScheduledRebootTime, out TimeSpan targetTimeOfDay))
            {
                targetTimeOfDay = new TimeSpan(4, 0, 0);
            }

            DateTime nowLocal = nowUtc.ToLocalTime();
            DateTime todayTargetLocal = nowLocal.Date.Add(targetTimeOfDay);

            if (nowLocal < todayTargetLocal)
            {
                return false;
            }

            if (meta.LastScheduledRebootTime.HasValue)
            {
                DateTime lastLocal = meta.LastScheduledRebootTime.Value.ToLocalTime();
                if (lastLocal.Date == nowLocal.Date && lastLocal >= todayTargetLocal)
                {
                    return false;
                }
            }

            if (sessionStart.HasValue)
            {
                DateTime startLocal = sessionStart.Value.ToLocalTime();
                if (startLocal.Date == nowLocal.Date && startLocal >= todayTargetLocal)
                {
                    return false;
                }
            }

            if (nowLocal > todayTargetLocal.AddMinutes(30))
            {
                return false;
            }

            return true;
        }
    }

    public static DateTime? GetNextRebootTimeUtc(InstanceMetadata meta, DateTime nowUtc)
    {
        if (meta == null || !meta.EnableScheduledReboot)
        {
            return null;
        }

        if (string.Equals(meta.ScheduledRebootMode, "Interval", StringComparison.OrdinalIgnoreCase))
        {
            int intervalHours = meta.ScheduledRebootIntervalHours > 0 ? meta.ScheduledRebootIntervalHours : 24;
            DateTime referenceTime = meta.LastScheduledRebootTime ?? nowUtc;
            return referenceTime.AddHours(intervalHours);
        }
        else
        {
            if (!TryParseTimeOfDay(meta.ScheduledRebootTime, out TimeSpan targetTimeOfDay))
            {
                targetTimeOfDay = new TimeSpan(4, 0, 0);
            }

            DateTime nowLocal = nowUtc.ToLocalTime();
            DateTime todayTargetLocal = nowLocal.Date.Add(targetTimeOfDay);

            DateTime nextLocal = nowLocal < todayTargetLocal
                ? todayTargetLocal
                : todayTargetLocal.AddDays(1);

            return nextLocal.ToUniversalTime();
        }
    }

    public static bool TryParseTimeOfDay(string? input, out TimeSpan timeOfDay)
    {
        timeOfDay = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();

        string[] formats = { "h\\:mm", "hh\\:mm", "H\\:mm", "HH\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss", "H\\:mm\\:ss", "HH\\:mm\\:ss" };
        if (TimeSpan.TryParseExact(input, formats, CultureInfo.InvariantCulture, out timeOfDay))
        {
            return true;
        }

        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out DateTime dt))
        {
            timeOfDay = dt.TimeOfDay;
            return true;
        }

        return false;
    }

    private async Task ExecuteScheduledRebootAsync(InstanceMetadata meta)
    {
        var cts = new CancellationTokenSource();
        if (!_inFlightReboots.TryAdd(meta.Id, cts))
        {
            cts.Dispose();
            return;
        }

        try
        {
            if (!_lifecycleService.IsRunning(meta.Id) || cts.Token.IsCancellationRequested)
            {
                return;
            }

            var process = _lifecycleService.GetProcess(meta.Id);
            int warningSeconds = meta.ScheduledRebootWarningSeconds > 0 ? meta.ScheduledRebootWarningSeconds : 60;

            if (meta.ScheduledRebootWarning && process != null && warningSeconds > 0)
            {
                await BroadcastRebootWarningAsync(process, $"Scheduled server reboot in {warningSeconds} seconds!");

                int remaining = warningSeconds;
                while (remaining > 0)
                {
                    if (cts.Token.IsCancellationRequested || !_lifecycleService.IsRunning(meta.Id))
                    {
                        _logger.LogInformation("Scheduled reboot countdown for '{ServerName}' was cancelled.", meta.Name);
                        return;
                    }

                    if (remaining is 60 or 30 or 15 or 10 or 5 or 4 or 3 or 2 or 1 && remaining < warningSeconds)
                    {
                        await BroadcastRebootWarningAsync(process, $"Server rebooting in {remaining} seconds!");
                    }

                    await Task.Delay(1000, cts.Token);
                    remaining--;
                }

                if (cts.Token.IsCancellationRequested || !_lifecycleService.IsRunning(meta.Id))
                {
                    return;
                }

                await BroadcastRebootWarningAsync(process, "Server is rebooting now...");
                await Task.Delay(500, cts.Token);
            }

            if (!_lifecycleService.IsRunning(meta.Id) || cts.Token.IsCancellationRequested)
            {
                return;
            }

            _logger.LogInformation("Executing restart for server '{ServerName}' ({InstanceId}) per maintenance schedule.", meta.Name, meta.Id);
            await _lifecycleService.RestartAsync(meta.Id);

            meta.LastScheduledRebootTime = DateTime.UtcNow;
            string? instancePath = _registry.GetPath(meta.Id);
            if (!string.IsNullOrEmpty(instancePath))
            {
                _instanceManager.SaveMetadata(meta, instancePath);
            }

            _notificationService.ShowInformation(
                "Server Rebooted",
                $"Server '{meta.Name}' was rebooted per maintenance schedule.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Scheduled reboot was cancelled for server '{ServerName}'.", meta.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform scheduled reboot for server '{ServerName}'.", meta.Name);
            _notificationService.ShowInformation(
                "Reboot Failed",
                $"Scheduled reboot failed for server '{meta.Name}'. Check logs for details.");
        }
        finally
        {
            _inFlightReboots.TryRemove(meta.Id, out _);
            cts.Dispose();
        }
    }

    private static async Task BroadcastRebootWarningAsync(IServerProcess process, string message)
    {
        try
        {
            process.EmitConsoleOutput($"[PocketMC] {message}");
            await process.WriteInputAsync($"say {message}");
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _lifecycleService.OnInstanceStateChanged -= HandleInstanceStateChanged;
        _timer.Stop();
        _timer.Dispose();
        AbortAllPendingReboots();
    }
}
