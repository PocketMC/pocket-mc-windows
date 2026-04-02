using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class ResourceMonitorService : IDisposable
    {
        private readonly Timer _timer;
        private int _listCommandTick = 0;
        
        public ConcurrentDictionary<Guid, InstanceMetrics> Metrics { get; } = new();
        public event Action? OnGlobalMetricsUpdated;

        private class ProcessTracker
        {
            public TimeSpan LastTotalProcessorTime { get; set; }
            public DateTime LastSampleTime { get; set; }
        }
        
        private readonly ConcurrentDictionary<Guid, ProcessTracker> _trackers = new();

        public ResourceMonitorService()
        {
            _timer = new Timer(OnTick, null, 2000, 2000);
        }

        private void OnTick(object? state)
        {
            var activeProcesses = ServerProcessManager.ActiveProcesses.Values
                .Where(p => p.State == ServerState.Online || p.State == ServerState.Starting).ToList();
            int count = activeProcesses.Count;
            if (count == 0)
            {
                _trackers.Clear();
                Metrics.Clear();
                // Ensure UI is notified if things drop to zero
                OnGlobalMetricsUpdated?.Invoke();
                return;
            }

            // Adaptive polling
            int interval = (count >= 7) ? 10000 : (count >= 4) ? 5000 : 2000;
            _timer.Change(interval, interval);

            _listCommandTick++;
            bool sendListCommand = (_listCommandTick * (interval / 1000.0)) >= 30;
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
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            
            // Clean up trackers for stopped instances
            var deadIds = _trackers.Keys.Except(activeProcesses.Select(p => p.InstanceId)).ToList();
            foreach (var id in deadIds)
            {
                _trackers.TryRemove(id, out _);
                Metrics.TryRemove(id, out _);
            }

            OnGlobalMetricsUpdated?.Invoke();
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
