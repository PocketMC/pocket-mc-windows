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

        private const string GlobalStatusUrl = "https://status.playit.gg/api/getMonitorList/AxwIw5PSiW";
        private const string DcStatusUrl = "https://dc.status.playit.gg/api/getMonitorList/rhIdVDK591";

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
                var globalTask = FetchStatusAsync(GlobalStatusUrl, cancellationToken);
                var dcTask = FetchStatusAsync(DcStatusUrl, cancellationToken);

                await Task.WhenAll(globalTask, dcTask);

                var globalStatus = globalTask.Result;
                var dcStatus = dcTask.Result;

                if (globalStatus?.Data != null)
                {
                    results.AddRange(globalStatus.Data);
                }

                if (dcStatus?.Data != null)
                {
                    results.AddRange(dcStatus.Data);
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
                // Add required headers if necessary, typically User-Agent
                request.Headers.Add("User-Agent", "PocketMC-DesktopApp");

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
