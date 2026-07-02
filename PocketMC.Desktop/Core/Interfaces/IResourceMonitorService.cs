using PocketMC.Domain.Models;
using PocketMC.Application.Instances.Services;
using PocketMC.Application.Instances.Providers;
using PocketMC.Desktop.Features.Mods;
using System;
using System.Collections.Concurrent;

namespace PocketMC.Desktop.Core.Interfaces
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

