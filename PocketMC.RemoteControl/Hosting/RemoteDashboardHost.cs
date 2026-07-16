using PocketMC.RemoteControl.Models;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Domain.Models;
using PocketMC.RemoteControl.Services;
using PocketMC.RemoteControl.Tunnels;
using PocketMC.Application.Services.Shell;
using System.Security.Claims;

namespace PocketMC.RemoteControl.Hosting;

public sealed class RemoteDashboardHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationState _applicationState;
    private readonly RemoteStatusService _statusService;
    private readonly RemoteInstanceControlService _instanceControlService;
    private readonly RemotePlayerActionService _playerActionService;
    private readonly RemoteConsoleWebSocketHandler _webSocketHandler;
    private readonly RemoteAuditLogService _auditLogService;
    private readonly RemoteRequestLimiter _requestLimiter;
    private readonly IServerLifecycleService _lifecycleService;
    private readonly RemoteTunnelManager _tunnelManager;
    private readonly LocalNetworkAddressService _localNetworkAddressService;
    private readonly RemoteAuthenticationService _authenticationService;
    private readonly ILogger<RemoteDashboardHost> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private WebApplication? _app;

    public RemoteDashboardHost(
        ApplicationState applicationState,
        RemoteStatusService statusService,
        RemoteInstanceControlService instanceControlService,
        RemotePlayerActionService playerActionService,
        RemoteConsoleWebSocketHandler webSocketHandler,
        RemoteAuditLogService auditLogService,
        RemoteRequestLimiter requestLimiter,
        IServerLifecycleService lifecycleService,
        RemoteTunnelManager tunnelManager,
        LocalNetworkAddressService localNetworkAddressService,
        RemoteAuthenticationService authenticationService,
        ILogger<RemoteDashboardHost> logger)
    {
        _applicationState = applicationState;
        _statusService = statusService;
        _instanceControlService = instanceControlService;
        _playerActionService = playerActionService;
        _webSocketHandler = webSocketHandler;
        _auditLogService = auditLogService;
        _requestLimiter = requestLimiter;
        _lifecycleService = lifecycleService;
        _tunnelManager = tunnelManager;
        _localNetworkAddressService = localNetworkAddressService;
        _authenticationService = authenticationService;
        _logger = logger;
    }

    public bool IsRunning => _app != null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_app != null || !_applicationState.Settings.RemoteControl.Enabled)
            {
                return;
            }

            RemoteControlSettings settings = _applicationState.Settings.RemoteControl;
            string bindAddress = "0.0.0.0";

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

            builder.Services.AddAuthentication("RemoteCookies")
                .AddCookie("RemoteCookies", options =>
                {
                    options.Cookie.Name = "PocketMCRemoteAuth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.ExpireTimeSpan = TimeSpan.FromHours(24);
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnValidatePrincipal = context =>
                    {
                        var expectedStamp = _applicationState.Settings.RemoteControl.SecurityStamp;
                        var actualStamp = context.Principal?.FindFirstValue("SecurityStamp");
                        if (actualStamp != expectedStamp)
                        {
                            context.RejectPrincipal();
                        }
                        return Task.CompletedTask;
                    };
                });
            builder.Services.AddAuthorization();

            WebApplication app = builder.Build();
            app.UseWebSockets();
            app.UseAuthentication();
            app.UseAuthorization();

            app.Use(async (context, next) =>
            {
                // Simple middleware to protect WebSockets
                if (context.Request.Path.StartsWithSegments("/ws") && _applicationState.Settings.RemoteControl.RequireAuthentication)
                {
                    if (!context.User.Identity?.IsAuthenticated ?? true)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                }
                await next(context);
            });

            MapStaticFiles(app);
            MapEndpoints(app);

            await app.StartAsync(cancellationToken);
            _app = app;
            _logger.LogInformation("Remote Control host started on {BindAddress}:{Port}.", bindAddress, settings.Port);
        }
        finally
        {
            _startGate.Release();
        }
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
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new EmbeddedFileProvider(typeof(RemoteDashboardHost).Assembly, "PocketMC.RemoteControl.Web"),
            RequestPath = "/remote",
                ContentTypeProvider = new FileExtensionContentTypeProvider(),
                OnPrepareResponse = ctx =>
                {
                    var path = ctx.Context.Request.Path.Value ?? "";
                    if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                    {
                        // Require revalidation for HTML entry points so changes are picked up immediately
                        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
                    }
                    else
                    {
                        // Cache other static assets (JS, CSS, images) for up to 1 day, easily bypassed via browser force-reload
                        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=86400";
                    }
                }
            });

        app.MapGet("/", () => Results.Redirect("/remote/index.html"));
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    private void MapEndpoints(WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            var path = context.HttpContext.Request.Path.Value;
            if (path == "/api/login" || path == "/api/status")
            {
                return await next(context);
            }

            if (_applicationState.Settings.RemoteControl.RequireAuthentication)
            {
                if (!context.HttpContext.User.Identity?.IsAuthenticated ?? true)
                {
                    return Results.Unauthorized();
                }
            }
            return await next(context);
        });

        api.MapPost("/login", async (HttpContext context) =>
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!_requestLimiter.TryConsume("login:remote", clientIp, 5, TimeSpan.FromMinutes(1)))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            var request = await ReadJsonAsync<RemoteLoginRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Password is required" });
            }

            var settings = _applicationState.Settings.RemoteControl;
            if (!settings.RequireAuthentication || _authenticationService.VerifyPassword(request.Password, settings.PasswordHash))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "Admin"),
                    new Claim("SecurityStamp", settings.SecurityStamp)
                };
                var claimsIdentity = new ClaimsIdentity(claims, "RemoteCookies");
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                };

                await context.SignInAsync("RemoteCookies", new ClaimsPrincipal(claimsIdentity), authProperties);
                return Results.Ok(new { success = true });
            }

            return Results.Unauthorized();
        });

        api.MapGet("/status", () => Results.Ok(BuildDashboardStatus()));

        api.MapGet("/instances", () => Results.Ok(_statusService.GetInstances()));

        api.MapGet("/instances/{instanceId:guid}/status", async (Guid instanceId) =>
        {
            RemoteInstanceStatusDto? status = await _statusService.GetInstanceStatusAsync(instanceId);
            return status == null ? Results.NotFound() : Results.Ok(status);
        });

        api.MapPost("/instances/{instanceId:guid}/start", async (Guid instanceId) =>
        {
            var result = await _instanceControlService.StartAsync(instanceId);
            _auditLogService.Log("remote", "instance.start", instanceId, null, result.Success, result.Success ? null : result.Message);
            return ToActionResult(result);
        });

        api.MapPost("/instances/{instanceId:guid}/stop", async (Guid instanceId) =>
        {
            var result = await _instanceControlService.StopAsync(instanceId);
            _auditLogService.Log("remote", "instance.stop", instanceId, null, result.Success, result.Success ? null : result.Message);
            return ToActionResult(result);
        });

        api.MapPost("/instances/{instanceId:guid}/restart", async (Guid instanceId) =>
        {
            var result = await _instanceControlService.RestartAsync(instanceId);
            _auditLogService.Log("remote", "instance.restart", instanceId, null, result.Success, result.Success ? null : result.Message);
            return ToActionResult(result);
        });

        api.MapGet("/instances/{instanceId:guid}/console/history", (Guid instanceId) =>
        {
            var process = _lifecycleService.GetProcess(instanceId);
            return process == null
                ? Results.NotFound()
                : Results.Ok(process.OutputBuffer.ToArray());
        });

        api.MapPost("/instances/{instanceId:guid}/console/command", async (HttpContext context, Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteConsoleCommands)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!_requestLimiter.TryConsume("console:remote", instanceId.ToString("D"), 30, TimeSpan.FromMinutes(1)))
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
            _auditLogService.Log("remote", "console.command", instanceId);
            return Results.Ok(new { sent = true });
        });


        api.MapGet("/instances/{instanceId:guid}/players", (Guid instanceId) =>
        {
            var process = _lifecycleService.GetProcess(instanceId);
            return process == null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    players = process.OnlinePlayerNames,
                    playerCount = process.PlayerCount
                });
        });


        foreach (string action in new[] { "kick", "ban", "pardon", "op", "deop" })
        {
            api.MapPost($"/instances/{{instanceId:guid}}/players/{{name}}/{action}", async (HttpContext context, Guid instanceId, string name) =>
            {
                RemotePlayerActionRequest? request = await ReadJsonAsync<RemotePlayerActionRequest>(context);
                RemoteControlActionResult result = await _playerActionService.ExecuteAsync(instanceId, name, action, request, "remote");
                return ToActionResult(result);
            });
        }

        app.Map("/ws/instances/{instanceId:guid}/console", async (HttpContext context, Guid instanceId) =>
        {
            await _webSocketHandler.HandleAsync(context, instanceId);
        });
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

