using PocketMC.Domain.Models;
using PocketMC.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Tests.RemoteControl;

public sealed class RemoteTunnelManagerTests
{
    [Fact]
    public async Task StartAsync_WhenProviderChanges_StopsExistingProviderAndStartsSelectedProvider()
    {
        var state = new ApplicationState();
        state.Settings.RemoteControl.TunnelProviderId = "cloudflared-quick";
        state.Settings.RemoteControl.AccessMode = RemoteAccessMode.CloudflaredQuickTunnel;
        var cloudflare = new FakeRemoteTunnelProvider("cloudflared-quick", "https://cloudflare.example");
        var other = new FakeRemoteTunnelProvider("other-provider", "https://other.example");
        var manager = new RemoteTunnelManager(state, new IRemoteTunnelProvider[] { cloudflare, other });

        RemoteTunnelStartResult first = await manager.StartAsync(CancellationToken.None);
        state.Settings.RemoteControl.AccessMode = RemoteAccessMode.LanOnly; // No provider needed, but we'll manually set provider to other-provider to test switching
        state.Settings.RemoteControl.TunnelProviderId = "other-provider";
        RemoteTunnelStartResult second = await manager.StartAsync(CancellationToken.None);

        Assert.Equal("https://cloudflare.example", first.PublicUrl);
        Assert.Equal("https://other.example", second.PublicUrl);
        Assert.Equal(1, cloudflare.StopCount);
        Assert.Equal(1, other.StartCount);
    }

    [Fact]
    public async Task StopAsync_WhenActiveProviderIsNull_LooksUpProviderFromSettings()
    {
        var state = new ApplicationState();
        state.Settings.RemoteControl.TunnelProviderId = "playit-https";
        var playit = new FakeRemoteTunnelProvider("playit-https", "https://playit.example");
        var manager = new RemoteTunnelManager(state, new IRemoteTunnelProvider[] { playit });

        // Act
        // _activeProvider is null since StartAsync was not called.
        await manager.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, playit.StopCount);
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



