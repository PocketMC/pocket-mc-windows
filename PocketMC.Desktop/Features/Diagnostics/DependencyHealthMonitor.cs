using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Diagnostics;

public enum DependencyHealthStatus { Unknown, Healthy, Degraded, Down }

public class DependencyHealth
{
    public string Name { get; set; } = string.Empty;
    public DependencyHealthStatus Status { get; set; }
    public TimeSpan Latency { get; set; }
    public DateTime LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DependencyHealthMonitor : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DependencyHealthMonitor> _logger;
    private readonly ConcurrentDictionary<string, DependencyHealth> _healthCache = new();
    private CancellationTokenSource? _cts;
    public event Action? HealthChanged;

    public DependencyHealthMonitor(IHttpClientFactory httpClientFactory, ILogger<DependencyHealthMonitor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void StartMonitoring()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
    }

    public DependencyHealth GetHealth(string name)
    {
        return _healthCache.TryGetValue(name, out var health)
            ? health
            : new DependencyHealth { Name = name, Status = DependencyHealthStatus.Unknown };
    }

    public IReadOnlyCollection<DependencyHealth> GetAllHealth() => _healthCache.Values.ToList();

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        // Setup initial default targets
        _healthCache["Playit.gg API"] = new DependencyHealth { Name = "Playit.gg API" };
        _healthCache["Adoptium API"] = new DependencyHealth { Name = "Adoptium API" };
        _healthCache["Modrinth API"] = new DependencyHealth { Name = "Modrinth API" };

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Ping endpoints in parallel
                await Task.WhenAll(
                    CheckEndpointAsync("Playit.gg API", "https://playit.gg/", token),
                    CheckEndpointAsync("Adoptium API", "https://api.adoptium.net/v3/info/release_names?page=0&size=1", token),
                    CheckEndpointAsync("Modrinth API", "https://api.modrinth.com/", token)
                );

                HealthChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health monitor loop encountered an exception.");
            }

            // Sleep 2.5 minutes between checks to avoid rate limiting
            await Task.Delay(TimeSpan.FromSeconds(150), token);
        }
    }

    private async Task CheckEndpointAsync(string name, string url, CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("HealthCheck");
            client.Timeout = TimeSpan.FromSeconds(12);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "PocketMC-Desktop/1.0 (HealthCheck)");

            var response = await client.SendAsync(request, token);
            sw.Stop();

            var health = _healthCache[name];
            health.LastChecked = DateTime.UtcNow;
            health.Latency = sw.Elapsed;

            if (response.IsSuccessStatusCode)
            {
                health.Status = sw.ElapsedMilliseconds > 3000 ? DependencyHealthStatus.Degraded : DependencyHealthStatus.Healthy;
                health.ErrorMessage = null;
            }
            else
            {
                health.Status = DependencyHealthStatus.Down;
                health.ErrorMessage = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            var health = _healthCache[name];
            health.Status = DependencyHealthStatus.Down;
            health.ErrorMessage = "Timeout";
            health.LastChecked = DateTime.UtcNow;
        }
        catch (Exception)
        {
            var health = _healthCache[name];
            health.Status = DependencyHealthStatus.Down;
            health.ErrorMessage = "Connection Failed";
            health.LastChecked = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
