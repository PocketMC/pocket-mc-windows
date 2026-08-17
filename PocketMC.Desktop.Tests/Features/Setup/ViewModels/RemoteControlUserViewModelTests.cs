using PocketMC.Desktop.Features.Setup.ViewModels;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Application.Services.Shell;
using PocketMC.RemoteControl.Services;
using PocketMC.RemoteControl.Hosting;
using PocketMC.RemoteControl.Tunnels;
using PocketMC.Desktop.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IO;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteControlUserViewModelTests
{
    private readonly RemoteControlSettingsViewModel _settingsVm;
    private readonly ApplicationState _state;
    private readonly SettingsManager _settingsManager;

    public RemoteControlUserViewModelTests()
    {
        _state = new ApplicationState();
        var tempConfigFile = Path.Combine(Path.GetTempPath(), $"PocketMC_Test_{Guid.NewGuid()}", "settings.json");
        _settingsManager = new SettingsManager(tempConfigFile);

        var authService = new RemoteAuthenticationService();
        var tunnelManager = new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>());
        var localNet = new LocalNetworkAddressService();
        var host = new RemoteDashboardHost(_state, null!, null!, null!, null!, null!, null!, null!, tunnelManager, localNet, authService, NullLogger<RemoteDashboardHost>.Instance);
        var coordinator = new RemoteControlCoordinator(_state, _settingsManager, host, tunnelManager, localNet);
        var dialogMock = new Mock<IDialogService>();

        _settingsVm = new RemoteControlSettingsViewModel(
            _state,
            _settingsManager,
            coordinator,
            dialogMock.Object,
            authService,
            null);
    }

    [Fact]
    public void RemoteControlUserViewModel_InitializesWithInstances_AndMapsSelectedState()
    {
        var instanceA = new InstanceMetadata { Id = Guid.NewGuid(), Name = "Server A", ServerType = "Paper", MinecraftVersion = "1.21" };
        var instanceB = new InstanceMetadata { Id = Guid.NewGuid(), Name = "Server B", ServerType = "Fabric", MinecraftVersion = "1.20" };

        var userModel = new RemoteControlUser
        {
            Username = "testuser",
            AllowAllInstances = false,
            AllowedInstanceIds = new List<Guid> { instanceA.Id }
        };

        var userVm = new RemoteControlUserViewModel(userModel, _settingsVm, new[] { instanceA, instanceB });

        Assert.False(userVm.AllowAllInstances);
        Assert.True(userVm.IsRestrictedInstances);
        Assert.Equal(2, userVm.AvailableInstances.Count);

        var itemA = userVm.AvailableInstances.First(i => i.InstanceId == instanceA.Id);
        var itemB = userVm.AvailableInstances.First(i => i.InstanceId == instanceB.Id);

        Assert.True(itemA.IsSelected);
        Assert.False(itemB.IsSelected);
    }

    [Fact]
    public void RemoteControlUserViewModel_SelectingInstance_UpdatesModelAllowedInstanceIds()
    {
        var instanceA = new InstanceMetadata { Id = Guid.NewGuid(), Name = "Server A", ServerType = "Paper" };
        var instanceB = new InstanceMetadata { Id = Guid.NewGuid(), Name = "Server B", ServerType = "Fabric" };

        var userModel = new RemoteControlUser
        {
            Username = "testuser",
            AllowAllInstances = false,
            AllowedInstanceIds = new List<Guid> { instanceA.Id }
        };

        var userVm = new RemoteControlUserViewModel(userModel, _settingsVm, new[] { instanceA, instanceB });

        var itemB = userVm.AvailableInstances.First(i => i.InstanceId == instanceB.Id);
        itemB.IsSelected = true;

        Assert.Contains(instanceA.Id, userModel.AllowedInstanceIds);
        Assert.Contains(instanceB.Id, userModel.AllowedInstanceIds);
        Assert.Equal(2, userModel.AllowedInstanceIds.Count);

        itemB.IsSelected = false;
        Assert.Contains(instanceA.Id, userModel.AllowedInstanceIds);
        Assert.DoesNotContain(instanceB.Id, userModel.AllowedInstanceIds);
        Assert.Single(userModel.AllowedInstanceIds);
    }

    [Fact]
    public void RemoteControlUserViewModel_TogglingAllowAllInstances_UpdatesModelProperty()
    {
        var userModel = new RemoteControlUser
        {
            Username = "testuser",
            AllowAllInstances = true
        };

        var userVm = new RemoteControlUserViewModel(userModel, _settingsVm);

        Assert.True(userVm.AllowAllInstances);
        Assert.False(userVm.IsRestrictedInstances);

        userVm.AllowAllInstances = false;

        Assert.False(userModel.AllowAllInstances);
        Assert.True(userVm.IsRestrictedInstances);
    }
}
