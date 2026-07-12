using PocketMC.Domain.Models;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Mods;
using System;
using System.Collections.Concurrent;

namespace PocketMC.Application.Interfaces
{
    public interface IResourceMonitorService
    {
        ConcurrentDictionary<Guid, InstanceMetrics> Metrics { get; }
        GlobalResourceSummary? CurrentSummary { get; }
        event EventHandler<InstanceMetricsUpdatedEventArgs>? InstanceMetricsUpdated;
        event EventHandler? GlobalMetricsUpdated;

        double GetTotalCommittedRamMb();
    }

    public class InstanceMetricsUpdatedEventArgs : EventArgs
    {
        public Guid InstanceId { get; }
        public InstanceMetrics Metrics { get; }

        public InstanceMetricsUpdatedEventArgs(Guid instanceId, InstanceMetrics metrics)
        {
            InstanceId = instanceId;
            Metrics = metrics;
        }
    }
}

