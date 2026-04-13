using System;

namespace PocketMC.Desktop.Models
{
    public class SessionSummary
    {
        public string FileName { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }
        public TimeSpan Duration { get; set; }
        public string Content { get; set; } = string.Empty;
        public string AiProvider { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
