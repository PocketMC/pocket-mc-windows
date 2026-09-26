using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Shell;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Instances;
using Xunit;

namespace PocketMC.Infrastructure.Tests.Instances;

using PocketMC.Infrastructure.Tests.TestSupport.Fixtures;

public class ServerRebootSchedulerServiceTests : IDisposable
{
    private readonly PortReliabilityTestWorkspace _workspace = new();
    private readonly Mock<IServerLifecycleService> _lifecycleServiceMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    public void Dispose() => _workspace.Dispose();

    private ServerRebootSchedulerService CreateService()
    {
        return new ServerRebootSchedulerService(
            _workspace.AppState,
            _workspace.Registry,
            _workspace.InstanceManager,
            _lifecycleServiceMock.Object,
            _notificationServiceMock.Object,
            NullLogger<ServerRebootSchedulerService>.Instance);
    }

    [Theory]
    [InlineData("04:00", 4, 0)]
    [InlineData("4:00", 4, 0)]
    [InlineData("16:30", 16, 30)]
    [InlineData("04:30:00", 4, 30)]
    [InlineData("4:15 PM", 16, 15)]
    [InlineData("4:15 AM", 4, 15)]
    public void TryParseTimeOfDay_ValidFormats_ParsesCorrectly(string input, int expectedHour, int expectedMinute)
    {
        bool success = ServerRebootSchedulerService.TryParseTimeOfDay(input, out TimeSpan timeOfDay);
        Assert.True(success);
        Assert.Equal(expectedHour, timeOfDay.Hours);
        Assert.Equal(expectedMinute, timeOfDay.Minutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("25:00")]
    [InlineData("4:65")]
    public void TryParseTimeOfDay_InvalidFormats_ReturnsFalse(string? input)
    {
        bool success = ServerRebootSchedulerService.TryParseTimeOfDay(input, out _);
        Assert.False(success);
    }

    [Fact]
    public void IsDue_WhenDisabled_ReturnsFalse()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = false
        };

        bool isDue = service.IsDue(meta, DateTime.UtcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_WhenServerNotRunning_ReturnsFalse()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true
        };

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(false);

        bool isDue = service.IsDue(meta, DateTime.UtcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_WhenServerNotOnline_ReturnsFalse()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Starting);

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);

        bool isDue = service.IsDue(meta, DateTime.UtcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_WhenWaitingToRestart_ReturnsFalse()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true
        };

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(true);

        bool isDue = service.IsDue(meta, DateTime.UtcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_WhenUptimeLessThanThreshold_ReturnsFalse()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Interval",
            ScheduledRebootIntervalHours = 1
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime now = DateTime.UtcNow;
        DateTime sessionStart = now.AddSeconds(-30); // only 30 seconds uptime

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStart);

        bool isDue = service.IsDue(meta, now);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_IntervalMode_DueWhenIntervalElapsed()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Interval",
            ScheduledRebootIntervalHours = 12
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime now = DateTime.UtcNow;
        DateTime sessionStart = now.AddHours(-13); // 13 hours uptime

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStart);

        bool isDue = service.IsDue(meta, now);
        Assert.True(isDue);
    }

    [Fact]
    public void IsDue_IntervalMode_NotDueBeforeIntervalElapsed()
    {
        var service = CreateService();
        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Interval",
            ScheduledRebootIntervalHours = 12
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime now = DateTime.UtcNow;
        DateTime sessionStart = now.AddHours(-6); // only 6 hours uptime

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStart);

        bool isDue = service.IsDue(meta, now);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_DailyMode_AtTargetTime_ReturnsTrue()
    {
        var service = CreateService();
        DateTime localNow = DateTime.Today.AddHours(4).AddMinutes(5); // 04:05 local
        DateTime utcNow = localNow.ToUniversalTime();

        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Daily",
            ScheduledRebootTime = "04:00"
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime sessionStartUtc = localNow.AddHours(-2).ToUniversalTime(); // started at 02:05 local

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStartUtc);

        bool isDue = service.IsDue(meta, utcNow);
        Assert.True(isDue);
    }

    [Fact]
    public void IsDue_DailyMode_BeforeTargetTime_ReturnsFalse()
    {
        var service = CreateService();
        DateTime localNow = DateTime.Today.AddHours(3).AddMinutes(55); // 03:55 local
        DateTime utcNow = localNow.ToUniversalTime();

        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Daily",
            ScheduledRebootTime = "04:00"
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime sessionStartUtc = localNow.AddHours(-2).ToUniversalTime();

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStartUtc);

        bool isDue = service.IsDue(meta, utcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_DailyMode_AlreadyRebootedToday_ReturnsFalse()
    {
        var service = CreateService();
        DateTime localNow = DateTime.Today.AddHours(4).AddMinutes(10);
        DateTime utcNow = localNow.ToUniversalTime();

        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Daily",
            ScheduledRebootTime = "04:00",
            LastScheduledRebootTime = DateTime.Today.AddHours(4).AddMinutes(1).ToUniversalTime()
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime sessionStartUtc = localNow.AddHours(-2).ToUniversalTime();

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStartUtc);

        bool isDue = service.IsDue(meta, utcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_DailyMode_ServerStartedAfterTargetTime_ReturnsFalse()
    {
        var service = CreateService();
        DateTime localNow = DateTime.Today.AddHours(4).AddMinutes(15);
        DateTime utcNow = localNow.ToUniversalTime();

        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Daily",
            ScheduledRebootTime = "04:00"
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        // Server booted at 04:02 local (after target 04:00)
        DateTime sessionStartUtc = DateTime.Today.AddHours(4).AddMinutes(2).ToUniversalTime();

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStartUtc);

        bool isDue = service.IsDue(meta, utcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void IsDue_DailyMode_PastToleranceWindow_ReturnsFalse()
    {
        var service = CreateService();
        // 05:00 local is > 30 minutes past 04:00
        DateTime localNow = DateTime.Today.AddHours(5).AddMinutes(0);
        DateTime utcNow = localNow.ToUniversalTime();

        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Daily",
            ScheduledRebootTime = "04:00"
        };

        var processMock = new Mock<IServerProcess>();
        processMock.Setup(p => p.State).Returns(ServerState.Online);

        DateTime sessionStartUtc = localNow.AddHours(-2).ToUniversalTime();

        _lifecycleServiceMock.Setup(x => x.IsRunning(meta.Id)).Returns(true);
        _lifecycleServiceMock.Setup(x => x.IsWaitingToRestart(meta.Id)).Returns(false);
        _lifecycleServiceMock.Setup(x => x.GetProcess(meta.Id)).Returns(processMock.Object);
        _lifecycleServiceMock.Setup(x => x.GetSessionStartTime(meta.Id)).Returns(sessionStartUtc);

        bool isDue = service.IsDue(meta, utcNow);
        Assert.False(isDue);
    }

    [Fact]
    public void GetNextRebootTimeUtc_DailyMode_CalculatesCorrectly()
    {
        DateTime localNow = DateTime.Today.AddHours(2);
        DateTime utcNow = localNow.ToUniversalTime();

        var meta = new InstanceMetadata
        {
            EnableScheduledReboot = true,
            ScheduledRebootMode = "Daily",
            ScheduledRebootTime = "04:00"
        };

        var nextReboot = ServerRebootSchedulerService.GetNextRebootTimeUtc(meta, utcNow);
        Assert.NotNull(nextReboot);

        DateTime expectedLocal = DateTime.Today.AddHours(4);
        Assert.Equal(expectedLocal.ToUniversalTime(), nextReboot.Value);
    }
}
