using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Result of attempting to resolve a tunnel for a server instance on start.
    /// </summary>
    public class TunnelResolutionResult
    {
        public enum TunnelStatus
        {
            /// <summary>Tunnel exists — public address is available.</summary>
            Found,
            /// <summary>No tunnel, but capacity exists — browser opened for creation.</summary>
            CreationStarted,
            /// <summary>Tunnel limit hit (4/4) — user must delete or change port.</summary>
            LimitReached,
            /// <summary>API call failed or token invalid — non-blocking warning.</summary>
            Error,
            /// <summary>Agent is not running or not claimed.</summary>
            AgentOffline
        }

        public TunnelStatus Status { get; set; }
        public string? PublicAddress { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsTokenInvalid { get; set; }
        public bool RequiresClaim { get; set; }
        public IReadOnlyList<TunnelData> ExistingTunnels { get; set; } = Array.Empty<TunnelData>();
    }

    /// <summary>
    /// Orchestrates tunnel resolution on every server start.
    /// Implements NET-06, NET-07, NET-08, NET-09, NET-10, NET-17.
    /// </summary>
    public class TunnelService
    {
        private readonly PlayitApiClient _apiClient;
        private readonly PlayitAgentService _agentService;
        private readonly ILogger<TunnelService> _logger;

        public TunnelService(PlayitApiClient apiClient, PlayitAgentService agentService, ILogger<TunnelService> logger)
        {
            _apiClient = apiClient;
            _agentService = agentService;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the tunnel address for a server instance's port.
        /// Called before every server start (NET-09: always fresh, never cached).
        /// </summary>
        public async Task<TunnelResolutionResult> ResolveTunnelAsync(int serverPort)
        {
            // Check if agent is online or actively starting up
            if (_agentService.State != PlayitAgentState.Connected && 
                _agentService.State != PlayitAgentState.Starting)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.AgentOffline,
                    ErrorMessage = "Playit agent is not connected."
                };
            }

            var result = await _apiClient.GetTunnelsAsync();

            // API failure — non-blocking warning (NET-17)
            if (!result.Success)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Error,
                    ErrorMessage = result.ErrorMessage,
                    IsTokenInvalid = result.IsTokenInvalid,
                    RequiresClaim = result.RequiresClaim
                };
            }

            // Path A — Tunnel already exists (NET-06)
            var matching = PlayitApiClient.FindTunnelForPort(result.Tunnels, serverPort);
            if (matching != null)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Found,
                    PublicAddress = matching.PublicAddress
                };
            }

            // Path C — Tunnel limit reached (NET-10)
            if (result.Tunnels.Count >= 4)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.LimitReached,
                    ExistingTunnels = result.Tunnels
                };
            }

            // Path B — No tunnel, capacity exists (NET-07)
            // Open the tunnel creation dashboard
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://playit.gg/account/setup/new-tunnel",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open the Playit tunnel creation page.");
            }

            return new TunnelResolutionResult
            {
                Status = TunnelResolutionResult.TunnelStatus.CreationStarted
            };
        }

        /// <summary>
        /// Polls the API every 5 seconds until a tunnel for the given port appears (NET-08).
        /// Returns the public address when found, or null on timeout/cancellation.
        /// </summary>
        public async Task<string?> PollForNewTunnelAsync(int serverPort, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5000, cancellationToken);

                var result = await _apiClient.GetTunnelsAsync();
                if (result.Success)
                {
                    var matching = PlayitApiClient.FindTunnelForPort(result.Tunnels, serverPort);
                    if (matching != null)
                    {
                        return matching.PublicAddress;
                    }
                }
            }

            return null;
        }
    }
}
