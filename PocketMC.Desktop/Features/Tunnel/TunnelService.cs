using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Networking;

namespace PocketMC.Desktop.Features.Tunnel
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
            /// <summary>Tunnel was automatically created and its address is available.</summary>
            AutoCreated,
            /// <summary>Tunnel limit hit (4/4) — user must delete or change port.</summary>
            LimitReached,
            /// <summary>API call failed or token invalid — non-blocking warning.</summary>
            Error,
            /// <summary>Agent is not running or not claimed.</summary>
            AgentOffline
        }

        public TunnelStatus Status { get; set; }
        public string? PublicAddress { get; set; }
        public string? NumericAddress { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsTokenInvalid { get; set; }
        public bool RequiresClaim { get; set; }
        public IReadOnlyList<TunnelData> ExistingTunnels { get; set; } = Array.Empty<TunnelData>();
        public PortFailureCode FailureCode { get; set; } = PortFailureCode.None;

        /// <summary>
        /// When set, indicates the failure was specifically from a v1_tunnels_create call.
        /// Contains the raw TunnelCreateErrorV1 code from the API.
        /// </summary>
        public string? CreateErrorCode { get; set; }

        public PortCheckResult? ToPortCheckResult(PortCheckRequest request)
        {
            PortFailureCode failureCode = FailureCode == PortFailureCode.None
                ? ClassifyFailureCode()
                : FailureCode;

            if (failureCode == PortFailureCode.None)
            {
                return null;
            }

            return new PortCheckResult(
                request,
                isSuccessful: false,
                canBindLocally: true,
                failureCode: failureCode,
                failureMessage: ErrorMessage ?? BuildDefaultFailureMessage(failureCode, request));
        }

        private PortFailureCode ClassifyFailureCode()
        {
            return Status switch
            {
                TunnelStatus.LimitReached => PortFailureCode.TunnelLimitReached,
                TunnelStatus.AgentOffline => PortFailureCode.PlayitAgentOffline,
                TunnelStatus.Error when IsTokenInvalid => PortFailureCode.PlayitTokenInvalid,
                TunnelStatus.Error when RequiresClaim => PortFailureCode.PlayitClaimRequired,
                TunnelStatus.Error => PortFailureCode.PublicReachabilityFailure,
                _ => PortFailureCode.None
            };
        }

        private static string BuildDefaultFailureMessage(PortFailureCode failureCode, PortCheckRequest request)
        {
            return failureCode switch
            {
                PortFailureCode.TunnelLimitReached => $"No Playit tunnel slots are available for {request.DisplayName} port {request.Port}.",
                PortFailureCode.PlayitAgentOffline => "The Playit agent is not connected.",
                PortFailureCode.PlayitTokenInvalid => "The Playit agent token is invalid or expired.",
                PortFailureCode.PlayitClaimRequired => "PocketMC needs a linked Playit agent before tunnel resolution can continue.",
                PortFailureCode.PublicReachabilityFailure => $"PocketMC could not resolve a public Playit address for {request.DisplayName} port {request.Port}.",
                _ => $"Tunnel resolution failed for port {request.Port}."
            };
        }
    }

    /// <summary>
    /// Orchestrates tunnel resolution on every server start.
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
        /// Called before every server start.
        /// </summary>
        public async Task<TunnelResolutionResult> ResolveTunnelAsync(int serverPort)
        {
            return await ResolveTunnelAsync(new PortCheckRequest(serverPort));
        }

        public async Task<TunnelResolutionResult> ResolveTunnelAsync(PortCheckRequest request)
        {
            if (_agentService.State == PlayitAgentState.ReauthRequired)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Error,
                    ErrorMessage = _agentService.LastErrorMessage ?? "The Playit credentials must be refreshed before PocketMC can resolve tunnels.",
                    IsTokenInvalid = true,
                    FailureCode = PortFailureCode.PlayitTokenInvalid
                };
            }

            if (_agentService.State == PlayitAgentState.AwaitingSetupCode)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Error,
                    ErrorMessage = "PocketMC must be linked to Playit before tunnel resolution can continue.",
                    RequiresClaim = true,
                    FailureCode = PortFailureCode.PlayitClaimRequired
                };
            }

            if (_agentService.State != PlayitAgentState.Connected &&
                _agentService.State != PlayitAgentState.Starting)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.AgentOffline,
                    ErrorMessage = "Playit agent is not connected.",
                    FailureCode = PortFailureCode.PlayitAgentOffline
                };
            }

            var result = await _apiClient.GetTunnelsAsync();

            if (!result.Success)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Error,
                    ErrorMessage = result.ErrorMessage,
                    IsTokenInvalid = result.IsTokenInvalid,
                    RequiresClaim = result.RequiresClaim,
                    FailureCode = ClassifyApiFailure(result)
                };
            }

            var matching = PlayitApiClient.FindTunnelForRequest(result.Tunnels, request);
            if (matching != null)
            {
                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Found,
                    PublicAddress = matching.PublicAddress,
                    NumericAddress = matching.NumericAddress
                };
            }
            // No matching tunnel exists — auto-create one via the API.
            // The API will reject the request if the account's tunnel limit is reached.
            return await AutoCreateTunnelAsync(request, result.Tunnels);
        }

        /// <summary>
        /// Automatically provisions a new PlayIt tunnel matching the given port request.
        /// On success, re-fetches tunnels to resolve the connect address.
        /// On failure, logs the error and returns a non-blocking error result.
        /// </summary>
        private async Task<TunnelResolutionResult> AutoCreateTunnelAsync(PortCheckRequest request, IReadOnlyList<TunnelData> existingTunnels)
        {
            bool isBedrock = request.Protocol == PortProtocol.Udp ||
                             request.BindingRole is PortBindingRole.BedrockServer
                                 or PortBindingRole.PocketMineServer
                                 or PortBindingRole.GeyserBedrock ||
                             request.Engine is PortEngine.BedrockDedicated
                                 or PortEngine.PocketMine
                                 or PortEngine.Geyser;

            string tunnelType = isBedrock ? "minecraft-bedrock" : "minecraft-java";
            string safeName = SanitizeTunnelName(request.InstanceName ?? request.DisplayName ?? "server");
            string tunnelName = $"{safeName}-{tunnelType}";

            _logger.LogInformation(
                "Auto-creating Playit tunnel: Name={TunnelName}, Type={TunnelType}, Port={Port}",
                tunnelName, tunnelType, request.Port);

            TunnelCreateResult createResult = await _apiClient.CreateTunnelAsync(tunnelName, tunnelType, request.Port);

            if (!createResult.Success)
            {
                // Check if the API rejected because the tunnel limit was hit
                if (createResult.IsLimitError)
                {
                    _logger.LogInformation(
                        "Playit tunnel limit reached for port {Port}. Upgrade required.",
                        request.Port);

                    return new TunnelResolutionResult
                    {
                        Status = TunnelResolutionResult.TunnelStatus.LimitReached,
                        ErrorMessage = "Tunnel limit reached. Visit playit.gg to upgrade.",
                        FailureCode = PortFailureCode.TunnelLimitReached,
                        CreateErrorCode = createResult.ErrorCode,
                        ExistingTunnels = existingTunnels
                    };
                }

                _logger.LogWarning(
                    "Playit auto-create failed for port {Port}: {Error}",
                    request.Port, createResult.ErrorMessage);

                return new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Error,
                    ErrorMessage = $"Automatic tunnel creation failed: {createResult.ErrorMessage}",
                    IsTokenInvalid = createResult.IsTokenInvalid,
                    RequiresClaim = createResult.RequiresClaim,
                    CreateErrorCode = createResult.ErrorCode
                };
            }

            _logger.LogInformation(
                "Playit tunnel created (id={TunnelId}). Resolving connect address...",
                createResult.TunnelId);

            // Re-fetch tunnels to resolve the connect address.
            // The newly created tunnel may take a moment to get a public allocation,
            // so we poll briefly.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }

                TunnelListResult refreshed = await _apiClient.GetTunnelsAsync();
                if (!refreshed.Success)
                {
                    continue;
                }

                TunnelData? created = refreshed.Tunnels.FirstOrDefault(t =>
                    t.Port == request.Port &&
                    (!t.Protocol.HasValue || t.Protocol.Value == request.Protocol ||
                     t.Protocol.Value == PortProtocol.TcpAndUdp || request.Protocol == PortProtocol.TcpAndUdp));

                if (created != null && !string.IsNullOrWhiteSpace(created.PublicAddress))
                {
                    return new TunnelResolutionResult
                    {
                        Status = TunnelResolutionResult.TunnelStatus.AutoCreated,
                        PublicAddress = created.PublicAddress,
                        NumericAddress = created.NumericAddress
                    };
                }
            }

            // Tunnel was created but we couldn't resolve a public address yet.
            // This is non-fatal — the address will appear once the allocation completes.
            _logger.LogWarning(
                "Tunnel was created for port {Port} but a public address is not yet available.",
                request.Port);

            return new TunnelResolutionResult
            {
                Status = TunnelResolutionResult.TunnelStatus.AutoCreated,
                ErrorMessage = "Tunnel created but public address is not yet available."
            };
        }

        /// <summary>
        /// Sanitizes an instance name for use as a PlayIt tunnel name.
        /// Keeps only ASCII alphanumeric characters and hyphens.
        /// </summary>
        private static string SanitizeTunnelName(string name)
        {
            char[] sanitized = name
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')
                .ToArray();
            string result = new string(sanitized).Trim('-');
            return string.IsNullOrWhiteSpace(result) ? "pocketmc-server" : result;
        }

        /// <summary>
        /// Polls the API every 5 seconds until a tunnel for the given port appears.
        /// Returns the public address when found, or null on timeout/cancellation.
        /// </summary>
        public async Task<string?> PollForNewTunnelAsync(int serverPort, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            return await PollForNewTunnelAsync(new PortCheckRequest(serverPort), cancellationToken, timeout);
        }

        public async Task<string?> PollForNewTunnelAsync(PortCheckRequest request, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            TunnelResolutionResult result = await PollForNewTunnelResultAsync(request, cancellationToken, timeout);
            return result.Status == TunnelResolutionResult.TunnelStatus.Found
                ? result.PublicAddress
                : null;
        }

        public async Task<TunnelResolutionResult> PollForNewTunnelResultAsync(PortCheckRequest request, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
            TunnelResolutionResult? lastFailure = null;

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5000, cancellationToken);

                var result = await _apiClient.GetTunnelsAsync();
                if (result.Success)
                {
                    var matching = PlayitApiClient.FindTunnelForRequest(result.Tunnels, request);
                    if (matching != null)
                    {
                        return new TunnelResolutionResult
                        {
                            Status = TunnelResolutionResult.TunnelStatus.Found,
                            PublicAddress = matching.PublicAddress,
                            NumericAddress = matching.NumericAddress
                        };
                    }

                    continue;
                }

                lastFailure = new TunnelResolutionResult
                {
                    Status = TunnelResolutionResult.TunnelStatus.Error,
                    ErrorMessage = result.ErrorMessage,
                    IsTokenInvalid = result.IsTokenInvalid,
                    RequiresClaim = result.RequiresClaim,
                    FailureCode = ClassifyApiFailure(result)
                };
            }

            return lastFailure ?? new TunnelResolutionResult
            {
                Status = TunnelResolutionResult.TunnelStatus.Error,
                FailureCode = PortFailureCode.PublicReachabilityFailure,
                ErrorMessage = $"Timed out waiting for a Playit public address for {request.DisplayName} port {request.Port}."
            };
        }

        private static PortFailureCode ClassifyApiFailure(TunnelListResult result)
        {
            if (result.IsTokenInvalid)
            {
                return PortFailureCode.PlayitTokenInvalid;
            }

            if (result.RequiresClaim)
            {
                return PortFailureCode.PlayitClaimRequired;
            }

            return PortFailureCode.PublicReachabilityFailure;
        }
    }
}
