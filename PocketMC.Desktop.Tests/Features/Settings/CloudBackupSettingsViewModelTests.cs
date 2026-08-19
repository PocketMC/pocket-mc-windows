using Moq;
using PocketMC.Application.Interfaces.Backups;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;
using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PocketMC.Desktop.Tests.Features.Settings;

public class CloudBackupSettingsViewModelTests
{
    [Fact]
    public async Task ConnectCommand_WhenNetworkFails_SanitizesProxyUrlAndShowsFriendlyError()
    {
        // Arrange
        var mockProvider = new Mock<ICloudBackupProvider>();
        mockProvider.Setup(p => p.ProviderType).Returns(CloudBackupProviderType.GoogleDrive);
        mockProvider.Setup(p => p.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No such host is known. (pocket-mc-proxy-n2qx.o1nrender.com:443)", new SocketException(11001)));

        var mockDialogService = new Mock<IDialogService>();
        string? shownTitle = null;
        string? shownMessage = null;
        DialogType? shownType = null;

        mockDialogService.Setup(d => d.ShowMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DialogType>()))
            .Callback<string, string, DialogType>((title, msg, type) =>
            {
                shownTitle = title;
                shownMessage = msg;
                shownType = type;
            });

        var vm = new CloudProviderViewModel(mockProvider.Object, mockDialogService.Object);

        // Act
        vm.ConnectCommand.Execute(null);

        // Wait a brief moment for async command
        await Task.Delay(100);

        // Assert
        Assert.Equal("Connection Failed", shownTitle);
        Assert.Equal(DialogType.Error, shownType);
        Assert.NotNull(shownMessage);
        Assert.DoesNotContain("pocket-mc-proxy", shownMessage);
        Assert.DoesNotContain("o1nrender.com", shownMessage);
        Assert.DoesNotContain("443", shownMessage);
        Assert.Equal("Unable to reach authentication services. Please check your internet connection and try again.", shownMessage);
    }

    [Fact]
    public async Task ConnectCommand_WhenGenericUrlLeakedInException_SanitizesMessage()
    {
        // Arrange
        var mockProvider = new Mock<ICloudBackupProvider>();
        mockProvider.Setup(p => p.ProviderType).Returns(CloudBackupProviderType.GoogleDrive);
        mockProvider.Setup(p => p.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Failed connecting to https://internal-secret-proxy.render.com/api/v1"));

        var mockDialogService = new Mock<IDialogService>();
        string? shownMessage = null;

        mockDialogService.Setup(d => d.ShowMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DialogType>()))
            .Callback<string, string, DialogType>((_, msg, _) => shownMessage = msg);

        var vm = new CloudProviderViewModel(mockProvider.Object, mockDialogService.Object);

        // Act
        vm.ConnectCommand.Execute(null);
        await Task.Delay(100);

        // Assert
        Assert.NotNull(shownMessage);
        Assert.DoesNotContain("internal-secret-proxy", shownMessage);
        Assert.Equal("Unable to reach authentication services. Please check your internet connection and try again.", shownMessage);
    }
}
