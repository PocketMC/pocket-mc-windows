using System;
using PocketMC.Desktop.Core.Mvvm;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Features.Dashboard
{
    public class DashboardMetricsVM : ViewModelBase
    {
        private readonly ResourceMonitorService _resourceMonitorService;

        public DashboardMetricsVM(ResourceMonitorService resourceMonitorService)
        {
            _resourceMonitorService = resourceMonitorService;
        }

        public void ApplyLiveMetrics(InstanceCardViewModel vm)
        {
            if (!vm.IsRunning)
            {
                vm.CpuText = "\u00b7 \u00b7 \u00b7";
                vm.RamText = "\u00b7 \u00b7 \u00b7";
                vm.PlayerStatus = "\u00b7 \u00b7 \u00b7";
                return;
            }

            if (_resourceMonitorService.Metrics.TryGetValue(vm.Id, out var metrics))
            {
                double maxRamGb = vm.Metadata.MaxRamMb / 1024.0;
                double usedRamGb = metrics.RamUsageMb / 1024.0;

                vm.CpuText = $"{Math.Round(metrics.CpuUsage):0}%";
                vm.RamText = $"{usedRamGb:F1} / {maxRamGb:F0} GB";
                vm.PlayerStatus = $"{metrics.PlayerCount} / {vm.MaxPlayers}";
            }
        }
    }
}
