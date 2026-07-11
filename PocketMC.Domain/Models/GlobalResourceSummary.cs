using System;

namespace PocketMC.Domain.Models
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
}
