using PocketMC.Desktop.Features.RemoteControl.Auth;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteAuthServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PocketMC.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExchangePairingToken_StoresOnlyHashesAndCannotBeReused()
    {
        var (service, manager, _) = CreateService();
        RemotePairingSession pairing = service.CreatePairingSession(TimeSpan.FromMinutes(2));

        RemoteExchangeResult exchange = service.ExchangePairingToken(pairing.Token, "Chrome on Android");

        Assert.True(exchange.Success);
        Assert.NotNull(exchange.DeviceToken);
        Assert.NotNull(exchange.DeviceId);

        var persisted = manager.Load();
        var device = Assert.Single(persisted.RemoteControl.PairedDevices);
        Assert.Equal("Chrome on Android", device.DisplayName);
        Assert.NotEmpty(device.TokenHash);
        Assert.NotEmpty(device.TokenSalt);
        Assert.DoesNotContain(exchange.DeviceToken, File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.True(service.ValidateDeviceToken(exchange.DeviceToken!).Success);

        RemoteExchangeResult second = service.ExchangePairingToken(pairing.Token, "Safari");
        Assert.False(second.Success);
        Assert.Equal(RemoteAuthFailure.InvalidPairingToken, second.Failure);

        persisted = manager.Load();
        Assert.Single(persisted.RemoteControl.PairedDevices);
    }

    [Fact]
    public void ExchangePairingToken_RejectsExpiredPairingSession()
    {
        var (service, _, _) = CreateService();
        RemotePairingSession pairing = service.CreatePairingSession(TimeSpan.FromSeconds(-1));

        RemoteExchangeResult result = service.ExchangePairingToken(pairing.Token, "Phone");

        Assert.False(result.Success);
        Assert.Equal(RemoteAuthFailure.ExpiredPairingToken, result.Failure);
    }

    [Fact]
    public void ValidateDeviceToken_RejectsMissingInvalidAndRevokedTokens()
    {
        var (service, _, _) = CreateService();
        RemotePairingSession pairing = service.CreatePairingSession(TimeSpan.FromMinutes(2));
        RemoteExchangeResult exchange = service.ExchangePairingToken(pairing.Token, "Phone");

        Assert.False(service.ValidateDeviceToken(null).Success);
        Assert.Equal(RemoteAuthFailure.InvalidDeviceToken, service.ValidateDeviceToken("not-valid").Failure);

        service.RevokeDevice(exchange.DeviceId!);

        RemoteValidationResult revoked = service.ValidateDeviceToken(exchange.DeviceToken!);
        Assert.False(revoked.Success);
        Assert.Equal(RemoteAuthFailure.RevokedDeviceToken, revoked.Failure);
    }

    [Fact]
    public void RevokeAllDevices_InvalidatesEveryToken()
    {
        var (service, _, _) = CreateService();
        RemoteExchangeResult first = service.ExchangePairingToken(
            service.CreatePairingSession(TimeSpan.FromMinutes(2)).Token,
            "Phone");
        RemoteExchangeResult second = service.ExchangePairingToken(
            service.CreatePairingSession(TimeSpan.FromMinutes(2)).Token,
            "Tablet");

        service.RevokeAllDevices();

        Assert.False(service.ValidateDeviceToken(first.DeviceToken!).Success);
        Assert.False(service.ValidateDeviceToken(second.DeviceToken!).Success);
    }

    private string SettingsPath => Path.Combine(_tempDirectory, "settings.json");

    private (RemoteAuthService Service, SettingsManager Manager, ApplicationState State) CreateService()
    {
        Directory.CreateDirectory(_tempDirectory);
        var manager = new SettingsManager(SettingsPath);
        var state = new ApplicationState();
        state.ApplySettings(manager.Load());
        var service = new RemoteAuthService(state, manager, new RemoteTokenHasher());
        return (service, manager, state);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
