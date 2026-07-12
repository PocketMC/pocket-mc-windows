using System;

namespace PocketMC.Domain.Models
{
    public enum DependencyHealthStatus { Unknown, Healthy, Degraded, Down }

    public class DependencyHealth
    {
        public string Name { get; set; } = string.Empty;
        public DependencyHealthStatus Status { get; set; }
        public TimeSpan Latency { get; set; }
        public DateTime LastChecked { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
