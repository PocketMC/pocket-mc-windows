using System;
using System.IO;
using System.Timers;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Global background service that checks all instances once per minute
    /// and triggers automated backups when their schedule interval has elapsed.
    /// </summary>
    public class BackupSchedulerService : IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly BackupService _backupService = new();
        private readonly string _appRoot;
        private bool _isProcessing;

        public BackupSchedulerService(string appRoot)
        {
            _appRoot = appRoot;
            _timer = new System.Timers.Timer(60_000); // Check every 60 seconds
            _timer.Elapsed += OnTimerTick;
            _timer.AutoReset = true;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private async void OnTimerTick(object? sender, ElapsedEventArgs e)
        {
            // Prevent re-entrant ticks
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                var serversDir = Path.Combine(_appRoot, "servers");
                if (!Directory.Exists(serversDir)) return;

                foreach (var dir in Directory.GetDirectories(serversDir))
                {
                    var metaFile = Path.Combine(dir, ".pocket-mc.json");
                    if (!File.Exists(metaFile)) continue;

                    try
                    {
                        var json = File.ReadAllText(metaFile);
                        var meta = System.Text.Json.JsonSerializer.Deserialize<InstanceMetadata>(json);
                        if (meta == null || meta.BackupIntervalHours <= 0) continue;

                        var lastBackup = meta.LastBackupTime ?? DateTime.MinValue;
                        var nextDue = lastBackup.AddHours(meta.BackupIntervalHours);

                        if (DateTime.UtcNow >= nextDue)
                        {
                            // Fire and forget on thread pool — errors are silently caught
                            await _backupService.RunBackupAsync(meta, dir);
                        }
                    }
                    catch
                    {
                        // Skip this instance on any error — don't crash the scheduler
                    }
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
