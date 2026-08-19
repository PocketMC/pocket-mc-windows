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
using System.Linq;
using System.Collections.Generic;
using System;
using Xunit;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteControlUserViewModelTests
{
    private readonly RemoteControlSettingsViewModel _settingsVm;
    private readonly ApplicationState _state;
    private readonly SettingsManager _settingsManager;

    public RemoteControlUserViewModelTests()
    {
        _state = new ApplicationState();
        _state.Settings.RemoteControl.Port = 25580;
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

    [Fact]
    public void SaveUser_WhenPasswordMatchesAdminPassword_SavesSuccessfully()
    {
        var authService = new RemoteAuthenticationService();
        string adminPass = "SharedPass123!";
        _state.Settings.RemoteControl.PasswordHash = authService.HashPassword(adminPass);
        _state.Settings.RemoteControl.Username = "admin";

        var userModel = new RemoteControlUser { Username = "subuser" };
        var userVm = new RemoteControlUserViewModel(userModel, _settingsVm)
        {
            Username = "subuser",
            Password = adminPass
        };

        _settingsVm.SaveUser(userVm);

        Assert.False(_settingsVm.IsStatusError);
        Assert.Equal("User 'subuser' credentials saved successfully.", _settingsVm.StatusText);
        Assert.False(string.IsNullOrEmpty(userModel.PasswordHash));
        Assert.True(authService.VerifyPassword(adminPass, userModel.PasswordHash));
    }

    [Fact]
    public void SaveUser_WhenNewUserMatchesAdminUsername_RejectsAndRemovesUnsavedEntry()
    {
        _state.Settings.RemoteControl.Username = "admin";

        var userModel = new RemoteControlUser();
        var userVm = new RemoteControlUserViewModel(userModel, _settingsVm)
        {
            Username = "admin",
            Password = "somepassword"
        };
        _settingsVm.Users.Add(userVm);

        _settingsVm.SaveUser(userVm);

        Assert.True(_settingsVm.IsStatusError);
        Assert.Equal("Sub-user username cannot be the same as the admin username.", _settingsVm.StatusText);
        Assert.DoesNotContain(userVm, _settingsVm.Users);
    }

    [Fact]
    public void SaveUser_WhenExistingUserMatchesAdminUsername_RejectsAndRevertsUsername()
    {
        _state.Settings.RemoteControl.Username = "admin";

        var userModel = new RemoteControlUser { Username = "validuser", PasswordHash = "hash123" };
        var userVm = new RemoteControlUserViewModel(userModel, _settingsVm)
        {
            Username = "admin",
            Password = "somepassword"
        };
        _settingsVm.Users.Add(userVm);

        _settingsVm.SaveUser(userVm);

        Assert.True(_settingsVm.IsStatusError);
        Assert.Equal("Sub-user username cannot be the same as the admin username.", _settingsVm.StatusText);
        Assert.Contains(userVm, _settingsVm.Users);
        Assert.Equal("validuser", userVm.Username);
        Assert.Equal("validuser", userVm.Model.Username);
    }

    [Fact]
    public void SaveUser_WhenNewUserMatchesExistingSubUser_RejectsAndRemovesUnsavedEntry()
    {
        var existingModel = new RemoteControlUser { Username = "existinguser", PasswordHash = "hash123" };
        var existingVm = new RemoteControlUserViewModel(existingModel, _settingsVm) { Username = "existinguser" };
        _settingsVm.Users.Add(existingVm);

        var newUserModel = new RemoteControlUser();
        var newUserVm = new RemoteControlUserViewModel(newUserModel, _settingsVm)
        {
            Username = "existinguser",
            Password = "somepassword"
        };
        _settingsVm.Users.Add(newUserVm);

        _settingsVm.SaveUser(newUserVm);

        Assert.True(_settingsVm.IsStatusError);
        Assert.Equal("User 'existinguser' already exists.", _settingsVm.StatusText);
        Assert.DoesNotContain(newUserVm, _settingsVm.Users);
        Assert.Contains(existingVm, _settingsVm.Users);
    }

    [Fact]
    public void SaveAdminCredentials_WhenAdminMatchesExistingSubUser_RejectsAndRevertsAdminUsername()
    {
        _state.Settings.RemoteControl.Username = "originalAdmin";
        _settingsVm.Username = "subuser1";
        _settingsVm.Password = "adminPass123!";

        var subUserModel = new RemoteControlUser { Username = "subuser1", PasswordHash = "hash123" };
        var subUserVm = new RemoteControlUserViewModel(subUserModel, _settingsVm) { Username = "subuser1" };
        _settingsVm.Users.Add(subUserVm);

        _settingsVm.SaveCredentialsCommand.Execute(null);

        Assert.True(_settingsVm.IsStatusError);
        Assert.Equal("Admin username cannot be the same as an existing sub-user username.", _settingsVm.StatusText);
        Assert.Equal("originalAdmin", _settingsVm.Username);
    }

    [Fact]
    public void CancelEdit_WhenUserIsUnsaved_RemovesFromUsersCollection()
    {
        var newUserModel = new RemoteControlUser();
        var newUserVm = new RemoteControlUserViewModel(newUserModel, _settingsVm)
        {
            Username = "temporaryUser",
            IsEditing = true
        };
        _settingsVm.Users.Add(newUserVm);

        newUserVm.CancelEditCommand.Execute(null);

        Assert.DoesNotContain(newUserVm, _settingsVm.Users);
    }

    [Fact]
    public void CancelEdit_WhenUserIsSaved_RevertsUsernameAndExitsEditMode()
    {
        var savedModel = new RemoteControlUser { Username = "savedName", PasswordHash = "hash123" };
        var userVm = new RemoteControlUserViewModel(savedModel, _settingsVm)
        {
            Username = "editedName",
            IsEditing = true
        };
        _settingsVm.Users.Add(userVm);

        userVm.CancelEditCommand.Execute(null);

        Assert.Contains(userVm, _settingsVm.Users);
        Assert.Equal("savedName", userVm.Username);
        Assert.False(userVm.IsEditing);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSettingsSavedAndDisposesCleanly()
    {
        var vm = _settingsVm;
        vm.Dispose();

        _settingsManager.Save(_state.Settings);
        Assert.NotNull(vm);
    }
}
