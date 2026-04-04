using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Services
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
        public string DisplayText => $"Global RAM: {Math.Round(CommittedRamMb)} MB / {Math.Round(TotalPhysicalRamMb)} MB";
    }

    public class ResourceMonitorService : IDisposable
    {
        private readonly ServerProcessManager _serverProcessManager;
        private readonly ILogger<ResourceMonitorService> _logger;
        private readonly Timer _timer;
        private readonly double _totalPhysicalRamMb;
        private int _tickInProgress;
        private int _listCommandTick = 0;
        
        public ConcurrentDictionary<Guid, InstanceMetrics> Metrics { get; } = new();
        public event Action? OnGlobalMetricsUpdated;
        private GlobalResourceSummary _currentSummary;
        public GlobalResourceSummary CurrentSummary => Volatile.Read(ref _currentSummary);

        private class ProcessTracker
        {
            public TimeSpan LastTotalProcessorTime { get; set; }
            public DateTime LastSampleTime { get; set; }
        }
        
        private readonly ConcurrentDictionary<Guid, ProcessTracker> _trackers = new();

        public ResourceMonitorService(ServerProcessManager serverProcessManager, ILogger<ResourceMonitorService> logger)
        {
            _serverProcessManager = serverProcessManager;
            _logger = logger;
            _totalPhysicalRamMb = (double)MemoryHelper.GetTotalPhysicalMemoryMb();
            _currentSummary = new GlobalResourceSummary(0, _totalPhysicalRamMb);
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
                    Volatile.Write(ref _currentSummary, new GlobalResourceSummary(0, _totalPhysicalRamMb));
                    // Ensure UI is notified if things drop to zero
                    OnGlobalMetricsUpdated?.Invoke();
                    return;
                }

                // Adaptive polling
                nextInterval = (count >= 7) ? 10000 : (count >= 4) ? 5000 : 2000;

                _listCommandTick++;
                bool sendListCommand = (_listCommandTick * (nextInterval / 1000.0)) >= 30;
                if (sendListCommand) _listCommandTick = 0;

                foreach (var sp in activeProcesses)
                {
                    Process? proc = sp.GetInternalProcess();
                    if (proc == null || proc.HasExited) continue;

                    var metric = Metrics.GetOrAdd(sp.InstanceId, _ => new InstanceMetrics());
                    var tracker = _trackers.GetOrAdd(sp.InstanceId, _ => new ProcessTracker { LastSampleTime = DateTime.UtcNow });

                    try
                    {
                        // Update RAM
                        proc.Refresh(); // Refresh the native Process properties
                        metric.RamUsageMb = proc.WorkingSet64 / (1024.0 * 1024.0);

                        // Update CPU
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

                        // Sync PlayerCount
                        metric.PlayerCount = sp.PlayerCount;

                        // Execute reconciling /list
                        if (sendListCommand && sp.State == ServerState.Online)
                        {
                            Task.Run(() => sp.WriteInputAsync("list"));
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        _logger.LogDebug(ex, "Skipping metric sample for instance {InstanceId} because the process is no longer accessible.", sp.InstanceId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogDebug(ex, "Skipping metric sample for instance {InstanceId} because the process is no longer valid.", sp.InstanceId);
                    }
                }
            
                // Clean up trackers for stopped instances
                var deadIds = _trackers.Keys.Except(activeProcesses.Select(p => p.InstanceId)).ToList();
                foreach (var id in deadIds)
                {
                    _trackers.TryRemove(id, out _);
                    Metrics.TryRemove(id, out _);
                }

                Volatile.Write(ref _currentSummary, new GlobalResourceSummary(Metrics.Values.Sum(m => m.RamUsageMb), _totalPhysicalRamMb));
                OnGlobalMetricsUpdated?.Invoke();
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
