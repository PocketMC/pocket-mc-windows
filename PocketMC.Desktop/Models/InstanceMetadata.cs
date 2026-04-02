using System;

namespace PocketMC.Desktop.Models
{
    public class InstanceMetadata
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ServerType { get; set; } = "Vanilla";
        public string MinecraftVersion { get; set; } = "1.20.4";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int MinRamMb { get; set; } = 1024;
        public int MaxRamMb { get; set; } = 4096;

        // Backup configuration
        // Backup configuration
        public int BackupIntervalHours { get; set; } = 0; // 0 = manual only
        public int MaxBackupsToKeep { get; set; } = 10;
        public DateTime? LastBackupTime { get; set; }
        
        // Auto-Restart configuration
        public bool EnableAutoRestart { get; set; } = false;
        public int MaxAutoRestarts { get; set; } = 3;
        public int AutoRestartDelaySeconds { get; set; } = 10;
    }
}
