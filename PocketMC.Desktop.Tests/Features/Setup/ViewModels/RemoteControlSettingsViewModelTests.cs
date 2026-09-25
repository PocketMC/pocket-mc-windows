using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Application.Services.Shell;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Setup.ViewModels;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;
using PocketMC.RemoteControl.Hosting;
using PocketMC.RemoteControl.Services;
using PocketMC.RemoteControl.Tunnels;
using Xunit;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteControlSettingsViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));
    private readonly ApplicationState _state;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteControlCoordinator _coordinator;
    private readonly RemoteAuthenticationService _authService;
    private readonly Mock<IDialogService> _dialogMock;

    public RemoteControlSettingsViewModelTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        _settingsManager = new SettingsManager(settingsPath);
        _state = new ApplicationState();
        _authService = new RemoteAuthenticationService();
        _dialogMock = new Mock<IDialogService>();

        var tunnelManager = new RemoteTunnelManager(_state, Array.Empty<IRemoteTunnelProvider>());
        var localNet = new PocketMC.RemoteControl.Services.LocalNetworkAddressService();
        var host = new RemoteDashboardHost(_state, null!, null!, null!, null!, null!, null!, null!, tunnelManager, localNet, _authService, NullLogger<RemoteDashboardHost>.Instance);
        _coordinator = new RemoteControlCoordinator(_state, _settingsManager, host, tunnelManager, localNet);
    }

    [Theory]
    [InlineData(RemoteAccessMode.LanOnly, "none")]
    [InlineData(RemoteAccessMode.CloudflaredQuickTunnel, "cloudflared-quick")]
    [InlineData(RemoteAccessMode.PlayitHttpsTunnel, "playit-https")]
    public void MapRemoteAccessModeToProviderId_ReturnsProviderId(RemoteAccessMode mode, string expectedProviderId)
    {
        Assert.Equal(expectedProviderId, RemoteControlSettingsViewModel.MapRemoteAccessModeToProviderId(mode));
    }

    [Fact]
    public void AccessModeOptions_ExposeOnlySupportedModesWithFriendlyLabels()
    {
        var options = RemoteControlSettingsViewModel.RemoteAccessModeOptions;

        Assert.Collection(
            options,
            option =>
            {
                Assert.Equal(RemoteAccessMode.CloudflaredQuickTunnel, option.Mode);
                Assert.Equal("Cloudflare Quick Tunnel", option.Label);
            },
            option =>
            {
                Assert.Equal(RemoteAccessMode.PlayitHttpsTunnel, option.Mode);
                Assert.Equal("PlayIt Premium HTTPS", option.Label);
            });
    }

    [Fact]
    public void SaveSettings_WhenPasswordNotEntered_PreservesExistingPasswordHashAndUsername()
    {
        _state.Settings.RemoteControl.Username = "adminuser";
        _state.Settings.RemoteControl.PasswordHash = _authService.HashPassword("existingpass123");
        _state.Settings.RemoteControl.Port = 25580;
        _settingsManager.Save(_state.Settings);

        var vm = new RemoteControlSettingsViewModel(_state, _settingsManager, _coordinator, _dialogMock.Object, _authService);
        Assert.True(vm.IsPasswordSet);
        Assert.False(vm.IsPasswordNotSet);
        Assert.Equal("adminuser", vm.Username);

        // Change port and trigger save
        vm.Port = 25585;
        bool saved = vm.SaveSettings();

        Assert.True(saved);
        Assert.Equal("adminuser", _state.Settings.RemoteControl.Username);
        Assert.NotNull(_state.Settings.RemoteControl.PasswordHash);
        Assert.True(_authService.VerifyPassword("existingpass123", _state.Settings.RemoteControl.PasswordHash));
    }

    [Fact]
    public void SaveSettings_WhenSubUserModified_DoesNotWipeAdminCredentials()
    {
        _state.Settings.RemoteControl.Username = "masteradmin";
        _state.Settings.RemoteControl.PasswordHash = _authService.HashPassword("supersecret");
        _settingsManager.Save(_state.Settings);

        var vm = new RemoteControlSettingsViewModel(_state, _settingsManager, _coordinator, _dialogMock.Object, _authService);
        var subUserModel = new RemoteControlUser { Username = "subuser", PasswordHash = _authService.HashPassword("subpass") };
        var subUserVm = new RemoteControlUserViewModel(subUserModel, vm);
        vm.Users.Add(subUserVm);

        // Toggle sub-user permission (which calls _parent.SaveSettings())
        subUserVm.AllowRemoteConsoleCommands = true;

        Assert.Equal("masteradmin", _state.Settings.RemoteControl.Username);
        Assert.NotNull(_state.Settings.RemoteControl.PasswordHash);
        Assert.True(_authService.VerifyPassword("supersecret", _state.Settings.RemoteControl.PasswordHash));
    }

    [Fact]
    public void SaveCredentials_WhenBothEmpty_ExplicitlyClearsCredentials()
    {
        _state.Settings.RemoteControl.Username = "admin";
        _state.Settings.RemoteControl.PasswordHash = _authService.HashPassword("pass");
        _settingsManager.Save(_state.Settings);

        var vm = new RemoteControlSettingsViewModel(_state, _settingsManager, _coordinator, _dialogMock.Object, _authService);
        vm.Username = "";
        vm.Password = "";

        vm.SaveCredentialsCommand.Execute(null);

        Assert.Null(_state.Settings.RemoteControl.Username);
        Assert.Null(_state.Settings.RemoteControl.PasswordHash);
        Assert.True(vm.IsPasswordNotSet);
        Assert.True(vm.IsUsernameNotSet);
    }

    [Fact]
    public void SaveCredentials_WhenUsernameUpdatedWithoutPassword_PreservesPasswordHash()
    {
        _state.Settings.RemoteControl.Username = "oldAdmin";
        _state.Settings.RemoteControl.PasswordHash = _authService.HashPassword("keepThisPass");
        _settingsManager.Save(_state.Settings);

        var vm = new RemoteControlSettingsViewModel(_state, _settingsManager, _coordinator, _dialogMock.Object, _authService);
        vm.Username = "newAdmin";
        vm.Password = "";

        vm.SaveCredentialsCommand.Execute(null);

        Assert.Equal("newAdmin", _state.Settings.RemoteControl.Username);
        Assert.NotNull(_state.Settings.RemoteControl.PasswordHash);
        Assert.True(_authService.VerifyPassword("keepThisPass", _state.Settings.RemoteControl.PasswordHash));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
} 