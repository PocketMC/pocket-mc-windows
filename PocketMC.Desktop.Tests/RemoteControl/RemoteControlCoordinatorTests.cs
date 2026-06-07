using Moq;
using PocketMC.Desktop.Features.RemoteControl.Auth;
using PocketMC.Desktop.Features.RemoteControl.Hosting;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteControlCoordinatorTests
{
    [Fact]
    public async Task StartHostAsync_FailureDoesNotPersistEnabledState()
    {
        var appState = new ApplicationState();
        appState.Settings.RemoteControl.Enabled = false;
        appState.Settings.RemoteControl.Port = -1; // Invalid port, will cause StartAsync to fail
        
        string tempFile = Path.GetTempFileName();
        try
        {
            var settingsManager = new SettingsManager(tempFile);
            
            var host = new RemoteDashboardHost(
                appState, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, 
                new Mock<Microsoft.Extensions.Logging.ILogger<RemoteDashboardHost>>().Object);
                
            var coordinator = new RemoteControlCoordinator(
                appState, settingsManager, null!, host, null!, null!);
                
            await Assert.ThrowsAnyAsync<Exception>(() => coordinator.StartHostAsync());
            
            Assert.False(appState.Settings.RemoteControl.Enabled);
            
            var reloadedSettings = settingsManager.Load();
            Assert.False(reloadedSettings.RemoteControl.Enabled);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
