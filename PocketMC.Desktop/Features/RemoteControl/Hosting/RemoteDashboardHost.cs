using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.RemoteControl.Auth;
using PocketMC.Desktop.Features.RemoteControl.Models;
using PocketMC.Desktop.Features.RemoteControl.Services;
using PocketMC.Desktop.Features.RemoteControl.Tunnels;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.RemoteControl.Hosting;

public sealed class RemoteDashboardHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AuthFailureWindow = TimeSpan.FromMinutes(5);

    private readonly ApplicationState _applicationState;
    private readonly RemoteAuthService _authService;
    private readonly RemoteStatusService _statusService;
    private readonly RemoteInstanceControlService _instanceControlService;
    private readonly RemotePlayerActionService _playerActionService;
    private readonly RemoteConsoleWebSocketHandler _webSocketHandler;
    private readonly RemoteAuditLogService _auditLogService;
    private readonly RemoteRequestLimiter _requestLimiter;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly RemoteTunnelManager _tunnelManager;
    private readonly LocalNetworkAddressService _localNetworkAddressService;
    private readonly ILogger<RemoteDashboardHost> _logger;
    private WebApplication? _app;

    public RemoteDashboardHost(
        ApplicationState applicationState,
        RemoteAuthService authService,
        RemoteStatusService statusService,
        RemoteInstanceControlService instanceControlService,
        RemotePlayerActionService playerActionService,
        RemoteConsoleWebSocketHandler webSocketHandler,
        RemoteAuditLogService auditLogService,
        RemoteRequestLimiter requestLimiter,
        IServerLifecycleService lifecycleService,
        RemoteTunnelManager tunnelManager,
        LocalNetworkAddressService localNetworkAddressService,
        ILogger<RemoteDashboardHost> logger)
    {
        _applicationState = applicationState;
        _authService = authService;
        _statusService = statusService;
        _instanceControlService = instanceControlService;
        _playerActionService = playerActionService;
        _webSocketHandler = webSocketHandler;
        _auditLogService = auditLogService;
        _requestLimiter = requestLimiter;
        _lifecycleService = lifecycleService;
        _tunnelManager = tunnelManager;
        _localNetworkAddressService = localNetworkAddressService;
        _logger = logger;
    }

    public bool IsRunning => _app != null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null || !_applicationState.Settings.RemoteControl.Enabled)
        {
            return;
        }

        RemoteControlSettings settings = _applicationState.Settings.RemoteControl;
        string bindAddress = UsesLoopbackOnlyForRemoteTunnel(settings.AccessMode)
            ? "127.0.0.1"
            : "0.0.0.0";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RemoteDashboardHost).Assembly.GetName().Name
        });
        builder.WebHost.UseUrls($"http://{bindAddress}:{settings.Port}");
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        WebApplication app = builder.Build();
        app.UseWebSockets();
        MapStaticFiles(app);
        MapEndpoints(app);

        await app.StartAsync(cancellationToken);
        _app = app;
        _logger.LogInformation("Remote Control host started on {BindAddress}:{Port}.", bindAddress, settings.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        WebApplication? app = _app;
        _app = null;
        if (app == null)
        {
            return;
        }

        await app.StopAsync(cancellationToken);
        await app.DisposeAsync();
        _logger.LogInformation("Remote Control host stopped.");
    }

    private void MapStaticFiles(WebApplication app)
    {
        string webRoot = Path.Combine(AppContext.BaseDirectory, "Features", "RemoteControl", "Web");
        if (Directory.Exists(webRoot))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(webRoot),
                RequestPath = "/remote",
                ContentTypeProvider = new FileExtensionContentTypeProvider()
            });
        }

        app.MapGet("/", () => Results.Redirect("/remote/index.html"));
        app.MapGet("/pair", (HttpContext context) =>
            Results.Redirect($"/remote/index.html{context.Request.QueryString}"));
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapPost("/api/pairing/exchange", async (HttpContext context) =>
        {
            string clientKey = GetClientKey(context);
            if (!_requestLimiter.TryConsume("pairing", clientKey, 5, TimeSpan.FromMinutes(1)))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            RemotePairingExchangeRequest? request = await ReadJsonAsync<RemotePairingExchangeRequest>(context);
            RemoteExchangeResult result = _authService.ExchangePairingToken(request?.PairingToken, request?.DeviceName);
            if (!result.Success)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new
            {
                result.DeviceToken,
                result.DeviceId
            });
        });

        app.MapGet("/api/status", (HttpContext context) =>
            WithAuth(context, auth => Results.Ok(BuildDashboardStatus())));

        app.MapGet("/api/instances", (HttpContext context) =>
            WithAuth(context, auth => Results.Ok(_statusService.GetInstances())));

        app.MapGet("/api/instances/{instanceId:guid}/status", async (HttpContext context, Guid instanceId) =>
            await WithAuthAsync(context, async auth =>
            {
                RemoteInstanceStatusDto? status = await _statusService.GetInstanceStatusAsync(instanceId);
                return status == null ? Results.NotFound() : Results.Ok(status);
            }));

        app.MapPost("/api/instances/{instanceId:guid}/start", async (HttpContext context, Guid instanceId) =>
            await WithAuthAsync(context, async auth =>
            {
                var result = await _instanceControlService.StartAsync(instanceId);
                _auditLogService.Log(auth.DeviceId, "instance.start", instanceId, null, result.Success, result.Success ? null : result.Message);
                return ToActionResult(result);
            }));

        app.MapPost("/api/instances/{instanceId:guid}/stop", async (HttpContext context, Guid instanceId) =>
            await WithAuthAsync(context, async auth =>
            {
                var result = await _instanceControlService.StopAsync(instanceId);
                _auditLogService.Log(auth.DeviceId, "instance.stop", instanceId, null, result.Success, result.Success ? null : result.Message);
                return ToActionResult(result);
            }));

        app.MapPost("/api/instances/{instanceId:guid}/restart", async (HttpContext context, Guid instanceId) =>
            await WithAuthAsync(context, async auth =>
            {
                var result = await _instanceControlService.RestartAsync(instanceId);
                _auditLogService.Log(auth.DeviceId, "instance.restart", instanceId, null, result.Success, result.Success ? null : result.Message);
                return ToActionResult(result);
            }));

        app.MapGet("/api/instances/{instanceId:guid}/console/history", (HttpContext context, Guid instanceId) =>
            WithAuth(context, auth =>
            {
                var process = _lifecycleService.GetProcess(instanceId);
                return process == null
                    ? Results.NotFound()
                    : Results.Ok(process.OutputBuffer.ToArray());
            }));

        app.MapPost("/api/instances/{instanceId:guid}/console/command", async (HttpContext context, Guid instanceId) =>
            await WithAuthAsync(context, async auth =>
            {
                if (!_applicationState.Settings.RemoteControl.AllowRemoteConsoleCommands)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                if (!_requestLimiter.TryConsume($"console:{auth.DeviceId}", instanceId.ToString("D"), 30, TimeSpan.FromMinutes(1)))
                {
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                }

                RemoteCommandRequest? request = await ReadJsonAsync<RemoteCommandRequest>(context);
                if (string.IsNullOrWhiteSpace(request?.Command))
                {
                    return Results.BadRequest(new { error = "Command is required." });
                }

                var process = _lifecycleService.GetProcess(instanceId);
                if (process == null || !_lifecycleService.IsRunning(instanceId))
                {
                    return Results.NotFound();
                }

                await process.WriteInputAsync(request.Command.Trim());
                _auditLogService.Log(auth.DeviceId, "console.command", instanceId);
                return Results.Ok(new { sent = true });
            }));

        app.MapPost("/api/instances/{instanceId:guid}/console/ticket", async (HttpContext context, Guid instanceId) =>
            await WithAuthAsync(context, async auth =>
            {
                string ticket = _authService.CreateWebSocketTicket(auth.DeviceId!, instanceId);
                return Results.Ok(new { ticket });
            }));

        app.MapGet("/api/instances/{instanceId:guid}/players", (HttpContext context, Guid instanceId) =>
            WithAuth(context, auth =>
            {
                var process = _lifecycleService.GetProcess(instanceId);
                return process == null
                    ? Results.NotFound()
                    : Results.Ok(new
                    {
                        players = process.OnlinePlayerNames,
                        playerCount = process.PlayerCount
                    });
            }));

        app.MapGet("/api/devices", (HttpContext context) =>
            WithAuth(context, auth => Results.Ok(new
            {
                devices = _authService.GetActiveDevices().Select(d => new
                {
                    id = d.Id,
                    name = d.DisplayName,
                    lastSeenAtUtc = d.LastSeenAtUtc,
                    createdAtUtc = d.CreatedAtUtc,
                    isCurrent = d.Id == auth.DeviceId
                })
            })));

        app.MapPost("/api/devices/revoke", async (HttpContext context) =>
            await WithAuthAsync(context, async auth =>
            {
                RemoteDeviceRevokeRequest? request = await ReadJsonAsync<RemoteDeviceRevokeRequest>(context);
                if (string.IsNullOrWhiteSpace(request?.DeviceId))
                {
                    return Results.BadRequest(new { error = "Device ID is required." });
                }

                bool revoked = _authService.RevokeDevice(request.DeviceId);
                if (!revoked)
                {
                    return Results.NotFound(new { error = "Device not found." });
                }

                return Results.Ok(new { ok = true });
            }));

        foreach (string action in new[] { "kick", "ban", "pardon", "op", "deop" })
        {
            app.MapPost($"/api/instances/{{instanceId:guid}}/players/{{name}}/{action}", async (HttpContext context, Guid instanceId, string name) =>
                await WithAuthAsync(context, async auth =>
                {
                    RemotePlayerActionRequest? request = await ReadJsonAsync<RemotePlayerActionRequest>(context);
                    RemoteControlActionResult result = await _playerActionService.ExecuteAsync(instanceId, name, action, request, auth.DeviceId);
                    return ToActionResult(result);
                }));
        }

        app.Map("/ws/instances/{instanceId:guid}/console", async (HttpContext context, Guid instanceId) =>
        {
            string? ticket = context.Request.Query["ticket"].FirstOrDefault();
            if (!_authService.ValidateWebSocketTicket(ticket, instanceId, out string deviceId))
            {
                await Results.Unauthorized().ExecuteAsync(context);
                return;
            }

            // Since we validated the ticket, we know the device is valid. We can inject it if needed,
            // but the web socket handler just streams logs right now and doesn't take input directly.
            await _webSocketHandler.HandleAsync(context, instanceId);
        });
    }

    private IResult WithAuth(HttpContext context, Func<RemoteValidationResult, IResult> handler)
    {
        IResult? authFailure = TryAuthenticate(context, allowQueryToken: false, out RemoteValidationResult auth);
        return authFailure ?? handler(auth);
    }

    private async Task<IResult> WithAuthAsync(HttpContext context, Func<RemoteValidationResult, Task<IResult>> handler)
    {
        IResult? authFailure = TryAuthenticate(context, allowQueryToken: false, out RemoteValidationResult auth);
        return authFailure ?? await handler(auth);
    }

    private IResult? TryAuthenticate(HttpContext context, bool allowQueryToken, out RemoteValidationResult validation)
    {
        validation = RemoteValidationResult.Failed(RemoteAuthFailure.InvalidDeviceToken);
        string clientKey = GetClientKey(context);
        if (_requestLimiter.IsBlocked("auth", clientKey, 20, AuthFailureWindow))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        string? token = GetBearerToken(context);
        if (string.IsNullOrWhiteSpace(token) && allowQueryToken)
        {
            token = context.Request.Query["token"].FirstOrDefault();
        }

        validation = _authService.ValidateDeviceToken(token);
        if (validation.Success)
        {
            return null;
        }

        _requestLimiter.RecordFailure("auth", clientKey, AuthFailureWindow);
        return Results.Unauthorized();
    }

    private static IResult ToActionResult(RemoteControlActionResult result)
    {
        if (result.Success)
        {
            return Results.Ok(new { ok = true });
        }

        return result.Failure switch
        {
            RemoteControlActionFailure.NotFound => Results.NotFound(new { error = result.Message }),
            RemoteControlActionFailure.NotRunning => Results.NotFound(new { error = result.Message }),
            RemoteControlActionFailure.Disabled => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.BadRequest(new { error = result.Message })
        };
    }

    private static string? GetBearerToken(HttpContext context)
    {
        string header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static string GetClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool UsesLoopbackOnlyForRemoteTunnel(RemoteAccessMode accessMode) =>
        accessMode is RemoteAccessMode.CloudflaredQuickTunnel;

    private RemoteDashboardStatus BuildDashboardStatus()
    {
        RemoteControlSettings settings = _applicationState.Settings.RemoteControl;
        RemoteTunnelStatus tunnelStatus = _tunnelManager.GetStatus();
        return new RemoteDashboardStatus
        {
            Enabled = settings.Enabled,
            HostRunning = IsRunning,
            Port = settings.Port,
            AccessMode = settings.AccessMode,
            LocalUrls = _localNetworkAddressService.GetLocalUrls(settings.Port),
            PublicUrl = tunnelStatus.PublicUrl,
            TunnelRunning = tunnelStatus.IsRunning,
            TunnelError = tunnelStatus.ErrorMessage,
            ActiveDeviceCount = settings.PairedDevices.Count(device => !device.RevokedAtUtc.HasValue),
            AllowRemoteConsoleCommands = settings.AllowRemoteConsoleCommands,
            AllowRemotePlayerActions = settings.AllowRemotePlayerActions
        };
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context)
    {
        if (context.Request.ContentLength == 0)
        {
            return default;
        }

        return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, JsonOptions);
    }
}
