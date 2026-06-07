using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteTunnelManagerTests
{
    [Fact]
    public async Task StartAsync_WhenProviderChanges_StopsExistingProviderAndStartsSelectedProvider()
    {
        var state = new ApplicationState();
        state.Settings.RemoteControl.TunnelProviderId = "cloudflared-quick";
        var cloudflare = new FakeRemoteTunnelProvider("cloudflared-quick", "https://cloudflare.example");
        var playit = new FakeRemoteTunnelProvider("playit-http", "https://playit.example");
        var manager = new RemoteTunnelManager(state, new IRemoteTunnelProvider[] { cloudflare, playit });

        RemoteTunnelStartResult first = await manager.StartAsync(CancellationToken.None);
        state.Settings.RemoteControl.AccessMode = RemoteAccessMode.PlayitHttpTunnel;
        state.Settings.RemoteControl.TunnelProviderId = "playit-http";
        RemoteTunnelStartResult second = await manager.StartAsync(CancellationToken.None);

        Assert.Equal("https://cloudflare.example", first.PublicUrl);
        Assert.Equal("https://playit.example", second.PublicUrl);
        Assert.Equal(1, cloudflare.StopCount);
        Assert.Equal(1, playit.StartCount);
    }

    private sealed class FakeRemoteTunnelProvider : IRemoteTunnelProvider
    {
        private readonly string _publicUrl;
        private bool _running;

        public FakeRemoteTunnelProvider(string id, string publicUrl)
        {
            Id = id;
            _publicUrl = publicUrl;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task<RemoteTunnelStartResult> StartAsync(RemoteTunnelStartRequest request, CancellationToken cancellationToken)
        {
            StartCount++;
            _running = true;
            return Task.FromResult(new RemoteTunnelStartResult
            {
                Success = true,
                PublicUrl = _publicUrl
            });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            _running = false;
            return Task.CompletedTask;
        }

        public RemoteTunnelStatus GetStatus() => new()
        {
            IsRunning = _running,
            PublicUrl = _running ? _publicUrl : null
        };
    }
}
