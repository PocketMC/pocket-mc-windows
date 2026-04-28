using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Features.Instances.Services
{
    public sealed class GlobalResourceSummary
    {
        public GlobalResourceSummary(double committedRamMb, double totalPhysicalRamMb)
        {
            CommittedRamMb = committedRamMb;
            TotalPhysicalRamMb = totalPhysicalRamMb;
        }

        public double CommittedRamMb { get; }
        public double TotalPhysicalRamMb { get; }
        public bool IsHighUsage => TotalPhysicalRamMb > 0 && CommittedRamMb > TotalPhysicalRamMb * 0.9;
        public string DisplayText => $"System RAM: {Math.Round(CommittedRamMb / 1024, 1)} / {Math.Round(TotalPhysicalRamMb / 1024, 1)} GB";
    }

    public class ResourceMonitorService : IResourceMonitorService, IDisposable
    {
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ILogger<ResourceMonitorService> _logger;
        private readonly Timer _timer;
        private readonly double _totalPhysicalRamMb;
        private int _tickInProgress;
        private int _listCommandTick = 0;

        public ConcurrentDictionary<Guid, InstanceMetrics> Metrics { get; } = new();
        private GlobalResourceSummary? _currentSummary;
        public GlobalResourceSummary? CurrentSummary => Volatile.Read(ref _currentSummary);

        public event EventHandler<InstanceMetricsUpdatedEventArgs>? InstanceMetricsUpdated;
        public event EventHandler? GlobalMetricsUpdated;

        private class ProcessTracker
        {
            public TimeSpan LastTotalProcessorTime { get; set; }
            public DateTime LastSampleTime { get; set; }
        }

        private readonly ConcurrentDictionary<Guid, ProcessTracker> _trackers = new();

        public ResourceMonitorService(
            ServerProcessManager serverProcessManager,
            ILogger<ResourceMonitorService> logger)
        {
            _serverProcessManager = serverProcessManager;
            _logger = logger;
            _totalPhysicalRamMb = (double)MemoryHelper.GetTotalPhysicalMemoryMb();
            double initialUsedMb = _totalPhysicalRamMb - (double)MemoryHelper.GetAvailablePhysicalMemoryMb();
            _currentSummary = new GlobalResourceSummary(initialUsedMb, _totalPhysicalRamMb);

            _logger.LogInformation("ResourceMonitorService initialized with system RAM info.");
            _timer = new Timer(OnTick, null, 2000, 2000);
        }

        private void OnTick(object? state)
        {
            if (Interlocked.CompareExchange(ref _tickInProgress, 1, 0) != 0)
            {
                return;
            }

            int nextInterval = 2000;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            try
            {
                var activeProcesses = _serverProcessManager.ActiveProcesses.Values
                    .Where(p => p.State == ServerState.Online || p.State == ServerState.Starting).ToList();
                int count = activeProcesses.Count;
                if (count == 0)
                {
                    _trackers.Clear();
                    Metrics.Clear();
                    double idleUsedMb = (double)MemoryHelper.GetTotalPhysicalMemoryMb() - (double)MemoryHelper.GetAvailablePhysicalMemoryMb();
                    Volatile.Write(ref _currentSummary, new GlobalResourceSummary(idleUsedMb, _totalPhysicalRamMb));
                    
                    NotifyGlobalMetricsUpdated();
                    return;
                }

                // Adaptive polling
                nextInterval = (count >= 7) ? 10000 : (count >= 4) ? 5000 : 2000;

                _listCommandTick++;
                bool sendListCommand = (_listCommandTick * (nextInterval / 1000.0)) >= 10;
                if (sendListCommand) _listCommandTick = 0;

                foreach (var sp in activeProcesses)
                {
                    Process? proc = sp.GetInternalProcess();
                    if (proc == null || proc.HasExited) continue;

                    var metric = Metrics.GetOrAdd(sp.InstanceId, _ => new InstanceMetrics());
                    var tracker = _trackers.GetOrAdd(sp.InstanceId, _ => new ProcessTracker { LastSampleTime = DateTime.UtcNow });

                    try
                    {
                        proc.Refresh(); 
                        metric.RamUsageMb = proc.WorkingSet64 / (1024.0 * 1024.0);

                        TimeSpan cpuTime = proc.TotalProcessorTime;
                        DateTime now = DateTime.UtcNow;

                        if (tracker.LastTotalProcessorTime != TimeSpan.Zero)
                        {
                            double cpuUsedMs = (cpuTime - tracker.LastTotalProcessorTime).TotalMilliseconds;
                            double totalTimeMs = (now - tracker.LastSampleTime).TotalMilliseconds;
                            if (totalTimeMs > 0)
                            {
                                double cpuUsage = (cpuUsedMs / (Environment.ProcessorCount * totalTimeMs)) * 100;
                                metric.CpuUsage = Math.Clamp(cpuUsage, 0, 100);
                            }
                        }

                        tracker.LastTotalProcessorTime = cpuTime;
                        tracker.LastSampleTime = now;
                        metric.PlayerCount = sp.PlayerCount;

                        // Notify individual instance update
                        InstanceMetricsUpdated?.Invoke(this, new InstanceMetricsUpdatedEventArgs(sp.InstanceId, metric));

                        if (sendListCommand && sp.State == ServerState.Online)
                        {
                            Task.Run(() => sp.WriteInputAsync("list"));
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        _logger.LogDebug(ex, "Skipping metric sample for instance {InstanceId}.", sp.InstanceId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogDebug(ex, "Skipping metric sample for instance {InstanceId}.", sp.InstanceId);
                    }
                }

                var deadIds = _trackers.Keys.Except(activeProcesses.Select(p => p.InstanceId)).ToList();
                foreach (var id in deadIds)
                {
                    _trackers.TryRemove(id, out _);
                    Metrics.TryRemove(id, out _);
                }

                double systemUsedMb = (double)MemoryHelper.GetTotalPhysicalMemoryMb() - (double)MemoryHelper.GetAvailablePhysicalMemoryMb();
                Volatile.Write(ref _currentSummary, new GlobalResourceSummary(systemUsedMb, _totalPhysicalRamMb));
                NotifyGlobalMetricsUpdated();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resource monitor tick failed.");
            }
            finally
            {
                Interlocked.Exchange(ref _tickInProgress, 0);
                _timer.Change(nextInterval, nextInterval);
            }
        }

        private void NotifyGlobalMetricsUpdated()
        {
            GlobalMetricsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public double GetTotalCommittedRamMb()
        {
            return Metrics.Values.Sum(m => m.RamUsageMb);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
