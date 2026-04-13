using System;
using System.Threading;
using System.Timers;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;
namespace PocketMC.Desktop.Features.Instances.Backups;

/// <summary>
/// Global background service that checks all instances once per minute
/// and triggers automated backups when their schedule interval has elapsed.
/// </summary>
public class BackupSchedulerService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly ApplicationState _applicationState;
    private readonly BackupService _backupService;
    private readonly InstanceRegistry _registry;
    private readonly ILogger<BackupSchedulerService> _logger;
    private int _isProcessing;

    public BackupSchedulerService(
        ApplicationState applicationState,
        BackupService backupService,
        InstanceRegistry registry,
        ILogger<BackupSchedulerService> logger)
    {
        _applicationState = applicationState;
        _backupService = backupService;
        _registry = registry;
        _logger = logger;
        _timer = new System.Timers.Timer(60_000); // Check every 60 seconds
        _timer.Elapsed += OnTimerTick;
        _timer.AutoReset = true;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private async void OnTimerTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Prevent re-entrant ticks
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!_applicationState.IsConfigured)
            {
                return;
            }

            foreach (var meta in _registry.GetAll())
            {
                var instancePath = _registry.GetPath(meta.Id);
                if (string.IsNullOrEmpty(instancePath))
                {
                    continue;
                }

                try
                {
                    if (meta.BackupIntervalHours <= 0) continue;

                    var lastBackup = meta.LastBackupTime ?? DateTime.MinValue;
                    var nextDue = lastBackup.AddHours(meta.BackupIntervalHours);

                    if (DateTime.UtcNow >= nextDue)
                    {
                        await _backupService.RunBackupAsync(meta, instancePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping scheduled backup for server {ServerName}.", meta.Name);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessing, 0);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
