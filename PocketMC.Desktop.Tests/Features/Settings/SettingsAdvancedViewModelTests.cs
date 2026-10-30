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
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("custom.key")]
    [InlineData(" custom.key ")]
    public void ValidateProperties_RejectsBlankOrDuplicateKeys(string key)
    {
        var vm = new SettingsAdvancedVM("unused", _workspace.ConfigurationService, () => { });
        vm.AdvancedProperties.Add(vm.CreatePropertyItem("custom.key", "first"));
        vm.AdvancedProperties.Add(vm.CreatePropertyItem(key, "second"));

        Assert.Throws<FormatException>(() => vm.ValidateProperties());
    }

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
}
