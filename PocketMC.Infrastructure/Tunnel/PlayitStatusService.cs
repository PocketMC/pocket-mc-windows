using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces.Tunnels;
using PocketMC.Domain.Models.Tunnel;

namespace PocketMC.Infrastructure.Tunnel
{
    public class PlayitStatusService : IPlayitStatusService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PlayitStatusService> _logger;

        private static string PrimaryStatusUrl => PocketMC.Infrastructure.Configuration.AppConfig.ProviderPlayitStatus;

        public PlayitStatusService(HttpClient httpClient, ILogger<PlayitStatusService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<PlayitStatusMonitor>> GetNetworkStatusAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<PlayitStatusMonitor>();

            try
            {
                var statusResponse = await FetchStatusAsync(PrimaryStatusUrl, cancellationToken);

                if (statusResponse?.Networks != null && statusResponse.Networks.Count > 0)
                {
                    foreach (var net in statusResponse.Networks)
                    {
                        if (string.IsNullOrWhiteSpace(net.Name)) continue;

                        string rawStatus = (net.Status ?? "online").ToLowerInvariant();
                        string statusClass = rawStatus switch
                        {
                            "online" => "success",
                            "re-routed" or "rerouted" or "degraded" => "warning",
                            "offline" or "down" or "outage" => "danger",
                            _ => "unknown"
                        };

                        string statusText = rawStatus switch
                        {
                            "online" => "Operational",
                            "re-routed" or "rerouted" => "Re-routed",
                            "degraded" => "Degraded",
                            "offline" or "down" or "outage" => "Outage",
                            _ => "Unknown"
                        };

                        results.Add(new PlayitStatusMonitor
                        {
                            Name = net.Name,
                            StatusClass = statusClass,
                            StatusText = statusText
                        });
                    }
                }
                else if (statusResponse?.Data != null)
                {
                    results.AddRange(statusResponse.Data);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch Playit network status.");
            }

            return results;
        }

        private async Task<PlayitStatusResponse?> FetchStatusAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", $"{PocketMC.Infrastructure.Configuration.AppConfig.AppName}-Desktop/{PocketMC.Infrastructure.Configuration.AppConfig.AppVersion}");

                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<PlayitStatusResponse>(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch status from {Url}", url);
                return null;
            }
        }
    }
}
