using System;
using Moq;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Features.Settings;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Settings;

using PocketMC.Desktop.Tests.TestSupport.Fixtures;

public class SettingsAdvancedViewModelTests : IDisposable
{
    private readonly PortReliabilityTestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void EnableScheduledReboot_WhenToggled_MarksDirtyAndNotifiesDependentProperties()
    {
        bool isDirty = false;
        var vm = new SettingsAdvancedVM(@"C:\fake\server", _workspace.ConfigurationService, () => isDirty = true);

        Assert.False(vm.EnableScheduledReboot);
        Assert.False(isDirty);

        vm.EnableScheduledReboot = true;

        Assert.True(vm.EnableScheduledReboot);
        Assert.True(isDirty);
        Assert.True(vm.IsDailyMode);
        Assert.False(vm.IsIntervalMode);
        Assert.True(vm.IsWarningSettingsEnabled);
        Assert.Contains("Daily at", vm.NextRebootSummary);
    }

    [Fact]
    public void ScheduledRebootMode_SwitchesBetweenDailyAndInterval()
    {
        bool isDirty = false;
        var vm = new SettingsAdvancedVM(@"C:\fake\server", _workspace.ConfigurationService, () => isDirty = true);
        vm.EnableScheduledReboot = true;
        isDirty = false;

        vm.ScheduledRebootMode = "Interval";
        vm.ScheduledRebootIntervalHours = "12";

        Assert.True(isDirty);
        Assert.False(vm.IsDailyMode);
        Assert.True(vm.IsIntervalMode);
        Assert.Equal("Every 12 hours", vm.NextRebootSummary);
    }

    [Fact]
    public void ScheduledRebootWarning_TogglingControlsWarningSettingsEnabled()
    {
        bool isDirty = false;
        var vm = new SettingsAdvancedVM(@"C:\fake\server", _workspace.ConfigurationService, () => isDirty = true);
        vm.EnableScheduledReboot = true;
        Assert.True(vm.IsWarningSettingsEnabled);
        Assert.True(isDirty);

        isDirty = false;
        vm.ScheduledRebootWarning = false;
        Assert.False(vm.IsWarningSettingsEnabled);
        Assert.True(isDirty);

        isDirty = false;
        vm.ScheduledRebootWarning = true;
        Assert.True(vm.IsWarningSettingsEnabled);
        Assert.True(isDirty);

        vm.EnableScheduledReboot = false;
        Assert.False(vm.IsWarningSettingsEnabled);
    }

    [Fact]
    public void RebootModes_ContainsDailyAndInterval()
    {
        var vm = new SettingsAdvancedVM(@"C:\fake\server", _workspace.ConfigurationService, () => { });
        Assert.Contains("Daily", vm.RebootModes);
        Assert.Contains("Interval", vm.RebootModes);
    }

    [Fact]
    public void ScheduledRebootTimeSpan_SynchronizesWithScheduledRebootTime()
    {
        bool isDirty = false;
        var vm = new SettingsAdvancedVM(@"C:\fake\server", _workspace.ConfigurationService, () => isDirty = true);

        // Default
        Assert.Equal("04:00", vm.ScheduledRebootTime);
        Assert.Equal(new TimeSpan(4, 0, 0), vm.ScheduledRebootTimeSpan);

        // Setting string updates TimeSpan
        vm.ScheduledRebootTime = "16:42";
        Assert.Equal(new TimeSpan(16, 42, 0), vm.ScheduledRebootTimeSpan);

        // Setting TimeSpan updates string and marks dirty
        isDirty = false;
        vm.ScheduledRebootTimeSpan = new TimeSpan(21, 30, 0);
        Assert.Equal("21:30", vm.ScheduledRebootTime);
        Assert.True(isDirty);
    }

    [Fact]
    public void TimePickerControl_InstantiationAndProperties()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var tp = new PocketMC.Desktop.Features.Settings.Controls.TimePickerControl
            {
                SelectedTime = new TimeSpan(16, 42, 0)
            };

            Assert.Equal(new TimeSpan(16, 42, 0), tp.SelectedTime);
            Assert.Equal("16", tp.SelectedHourText);
            Assert.Equal("42", tp.SelectedMinuteText);
            Assert.Equal(24, tp.HoursList.Count);
            Assert.Equal(60, tp.MinutesList.Count);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
