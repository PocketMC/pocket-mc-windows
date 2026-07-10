using PocketMC.Desktop.Infrastructure;
using PocketMC.Domain.Models;

namespace PocketMC.Desktop.Tests;

public sealed class WindowsStartupServiceTests
{
    [Fact]
    public void BuildStartupCommand_IncludesStartupAndMinimizedArgumentsWhenRequested()
    {
        var service = new WindowsStartupService(
            new RecordingStartupRegistry(),
            @"C:\Program Files\PocketMC\PocketMC.Desktop.exe");

        string command = service.BuildStartupCommand(minimized: true);

        Assert.Equal("\"C:\\Program Files\\PocketMC\\PocketMC.Desktop.exe\" --windows-startup --minimized", command);
    }

    [Fact]
    public void BuildStartupCommand_OmitsMinimizedArgumentWhenDisabled()
    {
        var service = new WindowsStartupService(
            new RecordingStartupRegistry(),
            @"C:\PocketMC\PocketMC.Desktop.exe");

        string command = service.BuildStartupCommand(minimized: false);

        Assert.Equal("\"C:\\PocketMC\\PocketMC.Desktop.exe\" --windows-startup", command);
    }

    [Fact]
    public void Apply_RegistersStartupWhenEnabled()
    {
        var registry = new RecordingStartupRegistry();
        var service = new WindowsStartupService(registry, @"C:\PocketMC\PocketMC.Desktop.exe");
        var settings = new AppSettings
        {
            StartWithWindows = true,
            StartMinimizedToTray = true
        };

        service.Apply(settings);

        Assert.Equal(WindowsStartupService.RunValueName, registry.LastSetName);
        Assert.Equal("\"C:\\PocketMC\\PocketMC.Desktop.exe\" --windows-startup --minimized", registry.Value);
        Assert.False(registry.DeleteCalled);
    }

    [Fact]
    public void Apply_DisablesStartupWhenSettingIsOff()
    {
        var registry = new RecordingStartupRegistry();
        var service = new WindowsStartupService(registry, @"C:\PocketMC\PocketMC.Desktop.exe");

        service.Apply(new AppSettings { StartWithWindows = false });

        Assert.Equal(WindowsStartupService.RunValueName, registry.LastDeletedName);
        Assert.True(registry.DeleteCalled);
        Assert.Null(registry.Value);
    }

    [Fact]
    public void IsBootTaskRegistered_WhenNotExist_ReturnsFalse()
    {
        var service = new WindowsStartupService(new RecordingStartupRegistry(), @"C:\PocketMC\PocketMC.Desktop.exe");

        bool isRegistered = service.IsBootTaskRegistered();

        Assert.False(isRegistered);
    }

    private sealed class RecordingStartupRegistry : IWindowsStartupRegistry
    {
        public string? Value { get; private set; }
        public string? LastSetName { get; private set; }
        public string? LastDeletedName { get; private set; }
        public bool DeleteCalled { get; private set; }

        public string? GetValue(string name) => Value;

        public void SetValue(string name, string value)
        {
            LastSetName = name;
            Value = value;
        }

        public void DeleteValue(string name)
        {
            LastDeletedName = name;
            DeleteCalled = true;
            Value = null;
        }
    }
}
