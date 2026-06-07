using System.Collections.Concurrent;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.RemoteControl.Auth;

public sealed record RemoteWebSocketTicket(
    string Ticket,
    Guid InstanceId,
    string DeviceId,
    DateTimeOffset ExpiresAtUtc);

public sealed class RemoteAuthService
{
    private static readonly TimeSpan DefaultPairingLifetime = TimeSpan.FromMinutes(2);

    private readonly ApplicationState _applicationState;
    private readonly SettingsManager _settingsManager;
    private readonly RemoteTokenHasher _tokenHasher;
    private readonly ConcurrentDictionary<string, RemotePairingSession> _pairingSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RemoteWebSocketTicket> _wsTickets = new(StringComparer.Ordinal);
    private readonly object _settingsLock = new();

    public event EventHandler? DevicesChanged;

    public RemoteAuthService(
        ApplicationState applicationState,
        SettingsManager settingsManager,
        RemoteTokenHasher tokenHasher)
    {
        _applicationState = applicationState;
        _settingsManager = settingsManager;
        _tokenHasher = tokenHasher;
    }

    public RemotePairingSession CreatePairingSession(TimeSpan? lifetime = null)
    {
        string token = _tokenHasher.GenerateToken();
        var session = new RemotePairingSession(
            token,
            DateTimeOffset.UtcNow.Add(lifetime ?? DefaultPairingLifetime));

        _pairingSessions[token] = session;
        return session;
    }

    public RemoteExchangeResult ExchangePairingToken(string? pairingToken, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(pairingToken) ||
            !_pairingSessions.TryRemove(pairingToken, out RemotePairingSession? session))
        {
            return RemoteExchangeResult.Failed(RemoteAuthFailure.InvalidPairingToken);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (session.ExpiresAtUtc <= now)
        {
            return RemoteExchangeResult.Failed(RemoteAuthFailure.ExpiredPairingToken);
        }

        session.LastExchangedAtUtc = now;
        string deviceToken = _tokenHasher.GenerateToken();
        RemoteTokenHash hash = _tokenHasher.Hash(deviceToken);

        var device = new RemoteDeviceSession
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = NormalizeDeviceName(deviceName),
            TokenHash = hash.Hash,
            TokenSalt = hash.Salt,
            CreatedAtUtc = now
        };

        lock (_settingsLock)
        {
            RemoteControlSettings settings = EnsureRemoteSettings(_applicationState.Settings);
            settings.PairedDevices.Add(device);
            _settingsManager.Save(_applicationState.Settings);
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);

        return RemoteExchangeResult.Successful(deviceToken, device.Id);
    }

    public RemoteValidationResult ValidateDeviceToken(string? deviceToken)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            return RemoteValidationResult.Failed(RemoteAuthFailure.InvalidDeviceToken);
        }

        RemoteDeviceSession[] devices;
        lock (_settingsLock)
        {
            devices = EnsureRemoteSettings(_applicationState.Settings).PairedDevices.ToArray();
        }

        foreach (RemoteDeviceSession device in devices)
        {
            if (!_tokenHasher.Verify(deviceToken, device.TokenSalt, device.TokenHash))
            {
                continue;
            }

            if (device.RevokedAtUtc.HasValue)
            {
                return RemoteValidationResult.Failed(RemoteAuthFailure.RevokedDeviceToken);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!device.LastSeenAtUtc.HasValue || (now - device.LastSeenAtUtc.Value).TotalMinutes > 5)
            {
                device.LastSeenAtUtc = now;
                lock (_settingsLock)
                {
                    _settingsManager.Save(_applicationState.Settings);
                }
            }
            else
            {
                device.LastSeenAtUtc = now;
            }

            return RemoteValidationResult.Successful(device.Id, device.DisplayName);
        }

        return RemoteValidationResult.Failed(RemoteAuthFailure.InvalidDeviceToken);
    }

    public bool RevokeDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        lock (_settingsLock)
        {
            RemoteDeviceSession? device = EnsureRemoteSettings(_applicationState.Settings)
                .PairedDevices
                .FirstOrDefault(x => string.Equals(x.Id, deviceId, StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                return false;
            }

            device.RevokedAtUtc ??= DateTimeOffset.UtcNow;
            _settingsManager.Save(_applicationState.Settings);
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyList<RemoteDeviceSession> GetActiveDevices()
    {
        lock (_settingsLock)
        {
            return EnsureRemoteSettings(_applicationState.Settings)
                .PairedDevices
                .Where(x => !x.RevokedAtUtc.HasValue)
                .ToList();
        }
    }

    public void RevokeAllDevices()
    {
        lock (_settingsLock)
        {
            DateTimeOffset revokedAt = DateTimeOffset.UtcNow;
            foreach (RemoteDeviceSession device in EnsureRemoteSettings(_applicationState.Settings).PairedDevices)
            {
                device.RevokedAtUtc ??= revokedAt;
            }

            _settingsManager.Save(_applicationState.Settings);
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static RemoteControlSettings EnsureRemoteSettings(PocketMC.Desktop.Models.AppSettings settings)
    {
        settings.RemoteControl ??= new RemoteControlSettings();
        settings.RemoteControl.PairedDevices ??= new List<RemoteDeviceSession>();
        settings.RemoteControl.PairedDevices.RemoveAll(static device => device == null);
        return settings.RemoteControl;
    }

    private static string NormalizeDeviceName(string? deviceName)
    {
        string normalized = string.IsNullOrWhiteSpace(deviceName)
            ? "Remote device"
            : deviceName.Trim();

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    public string CreateWebSocketTicket(string deviceId, Guid instanceId, TimeSpan? lifetime = null)
    {
        string ticket = _tokenHasher.GenerateToken();
        _wsTickets[ticket] = new RemoteWebSocketTicket(
            ticket,
            instanceId,
            deviceId,
            DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromSeconds(30)));
        return ticket;
    }

    public bool ValidateWebSocketTicket(string? ticket, Guid instanceId, out string deviceId)
    {
        deviceId = string.Empty;
        if (string.IsNullOrWhiteSpace(ticket) || !_wsTickets.TryRemove(ticket, out RemoteWebSocketTicket? wsTicket))
        {
            return false;
        }

        if (wsTicket.InstanceId != instanceId || wsTicket.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        deviceId = wsTicket.DeviceId;
        return true;
    }
}
