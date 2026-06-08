using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.RemoteControl.Tunnels;

public sealed class RemoteTunnelManager
{
    private readonly ApplicationState _applicationState;
    private readonly IReadOnlyDictionary<string, IRemoteTunnelProvider> _providers;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRemoteTunnelProvider? _activeProvider;

    public RemoteTunnelManager(ApplicationState applicationState, IEnumerable<IRemoteTunnelProvider> providers)
    {
        _applicationState = applicationState;
        _providers = providers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
    }

    public RemoteTunnelStatus GetStatus() =>
        _activeProvider?.GetStatus() ?? new RemoteTunnelStatus();

    public async Task<RemoteTunnelStartResult> StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RemoteControlSettings settings = _applicationState.Settings.RemoteControl;
            if (_activeProvider?.GetStatus().IsRunning == true &&
                string.Equals(_activeProvider.Id, settings.TunnelProviderId, StringComparison.OrdinalIgnoreCase))
            {
                RemoteTunnelStatus status = _activeProvider.GetStatus();
                return new RemoteTunnelStartResult
                {
                    Success = true,
                    PublicUrl = status.PublicUrl
                };
            }

            if (_activeProvider?.GetStatus().IsRunning == true)
            {
                await _activeProvider.StopAsync(cancellationToken);
                _activeProvider = null;
            }

            if (!_providers.TryGetValue(settings.TunnelProviderId, out IRemoteTunnelProvider? provider))
            {
                return RemoteTunnelStartResult.Failed($"Remote tunnel provider '{settings.TunnelProviderId}' is not available.");
            }

            string localUrl = $"http://127.0.0.1:{settings.Port}";
            
            _activeProvider = provider;
            
            RemoteTunnelStartResult result = await provider.StartAsync(
                new RemoteTunnelStartRequest
                {
                    LocalPort = settings.Port,
                    LocalUrl = localUrl
                },
                cancellationToken);

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var provider = _activeProvider;

            if (provider == null)
            {
                var providerId = _applicationState.Settings.RemoteControl.TunnelProviderId;
                _providers.TryGetValue(providerId, out provider);
            }

            if (provider != null)
            {
                await provider.StopAsync(cancellationToken);
            }

            _activeProvider = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
