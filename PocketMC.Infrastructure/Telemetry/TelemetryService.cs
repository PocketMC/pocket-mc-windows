using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.Instances;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Instances;
using PocketMC.Application.Services.Shell;

using PocketMC.Application.Interfaces;

namespace PocketMC.Infrastructure.Telemetry;

public sealed class TelemetryService : ITelemetryService, IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly ServerProcessManager _processManager;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelemetryService> _logger;

    private readonly CancellationTokenSource _cts = new();
    private Task? _backgroundTask;
    private bool _disposed;
    private string _cachedCountry = "Unknown";
    private bool _hasFetchedCountry = false;

    public TelemetryService(
        SettingsManager settingsManager,
        ServerProcessManager processManager,
        InstanceRegistry instanceRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<TelemetryService> logger)
    {
        _settingsManager = settingsManager;
        _processManager = processManager;
        _instanceRegistry = instanceRegistry;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void Initialize()
    {
        _logger.LogInformation("Initializing TelemetryService...");
        var settings = _settingsManager.Load();

        // Ensure Client ID exists
        if (settings.TelemetryClientId == null || settings.TelemetryClientId == Guid.Empty)
        {
            settings.TelemetryClientId = Guid.NewGuid();
            _settingsManager.Save(settings);
            _logger.LogInformation("Generated new Telemetry ClientId: {ClientId}", settings.TelemetryClientId);
        }

        // Hook into process state changes to send real-time updates
        _processManager.OnInstanceStateChanged += OnInstanceStateChanged;

        // Start background reporting loop
        _backgroundTask = Task.Run(ReportingLoopAsync);
    }

    public void Shutdown()
    {
        _logger.LogInformation("Shutting down TelemetryService...");
        _processManager.OnInstanceStateChanged -= OnInstanceStateChanged;

        // Send a final heartbeat to tell the backend the app is closed
        try
        {
            var settings = _settingsManager.Load();
            if (settings.EnableTelemetry)
            {
                Task.Run(() => SendHeartbeatAsync(settings, isAppClosed: true)).Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send final telemetry heartbeat: {ex}");
        }

        try
        {
            _cts.Cancel();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to cancel telemetry background task: {ex}");
        }

        WaitForBackgroundTaskToStop();
    }

    private void OnInstanceStateChanged(Guid instanceId, ServerState state)
    {
        var settings = _settingsManager.Load();
        if (settings.EnableTelemetry)
        {
            // Fire-and-forget sending the updated heartbeat
            _ = Task.Run(() => SendHeartbeatAsync(settings));
        }
    }

    private async Task ReportingLoopAsync()
    {
        // 0. Fetch Country Location once
        if (!_hasFetchedCountry)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var res = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("http://ip-api.com/json/?fields=country");
                if (res.TryGetProperty("country", out var cc) && cc.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    _cachedCountry = cc.GetString() ?? "Unknown";
                }
                _hasFetchedCountry = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch country from ip-api.");
            }
        }

        // 1. Check/Report one-time installation
        await EnsureInstallReportedAsync();

        // 2. Loop every 5 minutes for heartbeat telemetry
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var settings = _settingsManager.Load();
                if (settings.EnableTelemetry)
                {
                    await SendHeartbeatAsync(settings);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run periodic telemetry heartbeat.");
            }

            try
            {
                await timer.WaitForNextTickAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EnsureInstallReportedAsync()
    {
        var settings = _settingsManager.Load();
        if (settings.HasReportedInstall)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Reporting installation/upgrade to telemetry backend...");

            // Check if there are pre-existing configurations or servers in the app root to detect upgrade
            bool isUpgrade = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.AppRootPath) && Directory.Exists(settings.AppRootPath))
                {
                    var files = Directory.GetFileSystemEntries(settings.AppRootPath);
                    isUpgrade = files.Length > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to inspect app root for upgrades; defaulting isUpgrade to false.");
            }

            var payload = new
            {
                clientId = settings.TelemetryClientId.ToString(),
                isUpgrade = isUpgrade,
                country = _cachedCountry
            };

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await PostTelemetryAsJsonAsync(client, "/api/telemetry/install", payload, _cts.Token);
            if (response.IsSuccessStatusCode)
            {
                settings.HasReportedInstall = true;
                _settingsManager.Save(settings);
                _logger.LogInformation("Installation/upgrade reported successfully.");
            }
            else
            {
                _logger.LogWarning("Failed to report install to telemetry server. Status code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reporting installation/upgrade.");
        }
    }

    private async Task SendHeartbeatAsync(AppSettings settings, bool isAppClosed = false)
    {
        try
        {
            var activeProcesses = isAppClosed ? new System.Collections.Generic.List<ServerProcess>() : _processManager.ActiveProcesses.Values
                .Where(p => p.State == ServerState.Online ||
                            p.State == ServerState.Starting ||
                            p.State == ServerState.Stopping)
                .ToList();

            var activeServerTypes = activeProcesses
                .Select(p => _instanceRegistry.GetById(p.InstanceId)?.ServerType)
                .Where(type => !string.IsNullOrEmpty(type))
                .Cast<string>()
                .Distinct()
                .ToList();

            var version = AppConfig.AppVersion;

            var payload = new
            {
                clientId = settings.TelemetryClientId.ToString(),
                isAppOpen = !isAppClosed,
                isServerRunning = activeProcesses.Count > 0,
                activeServerCount = activeProcesses.Count,
                activeServerTypes = activeServerTypes,
                appVersion = version,
                country = _cachedCountry
            };

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await PostTelemetryAsJsonAsync(client, "/api/telemetry/report", payload, _cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Failed to post telemetry heartbeat. Status code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error posting telemetry heartbeat.");
        }
    }

    public async Task ReportServerActionAsync(string action)
    {
        try
        {
            var settings = _settingsManager.Load();
            if (!settings.EnableTelemetry) return;

            var payload = new
            {
                clientId = settings.TelemetryClientId.ToString(),
                action = action
            };

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await PostTelemetryAsJsonAsync(client, "/api/telemetry/server-action", payload, _cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Failed to post telemetry server action. Status code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error posting telemetry server action.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
        _cts.Dispose();
    }

    private void WaitForBackgroundTaskToStop()
    {
        var task = _backgroundTask;
        if (task == null || task.IsCompleted)
        {
            return;
        }

        try
        {
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Telemetry background task did not stop cleanly during shutdown.");
        }
    }

        private async Task<HttpResponseMessage> PostTelemetryAsJsonAsync<TValue>(HttpClient client, string path, TValue payload, CancellationToken ct = default)
        {
            var proxies = AppConfig.TelemetryProxies;
            Exception? lastException = null;

            foreach (var proxy in proxies)
            {
                if (string.IsNullOrWhiteSpace(proxy)) continue;

                try
                {
                    var response = await client.PostAsJsonAsync($"{proxy.TrimEnd('/')}{path}", payload, ct);
                    if (response.IsSuccessStatusCode)
                        return response;
                    
                    if (proxy == proxies[proxies.Count - 1]) return response;
                    response.Dispose();
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    _logger.LogDebug(ex, "Proxy {Proxy} request failed, trying next...", proxy);
                    lastException = ex;
                }
            }

            if (lastException != null) throw lastException;
            throw new HttpRequestException("All telemetry proxies failed.");
        }
}

