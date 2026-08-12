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
using PocketMC.Application.Services.Instances;
using PocketMC.Infrastructure.Mods;
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
    private readonly InstanceRegistry? _instanceRegistry;
    private readonly ServerConfigurationService? _serverConfigurationService;
    private readonly PocketMC.Infrastructure.Backups.BackupService? _backupService;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _activeBackups = new();
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
        ILogger<RemoteDashboardHost> logger,
        InstanceRegistry? instanceRegistry = null,
        ServerConfigurationService? serverConfigurationService = null,
        PocketMC.Infrastructure.Backups.BackupService? backupService = null)
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
        _instanceRegistry = instanceRegistry;
        _serverConfigurationService = serverConfigurationService;
        _backupService = backupService;
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

        api.MapGet("/instances/{instanceId:guid}/properties", (Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteServerSettings)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var metadata = _instanceRegistry?.GetById(instanceId);
            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (metadata == null || string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
            {
                return Results.NotFound();
            }

            var config = _serverConfigurationService?.Load(metadata, serverDir);
            if (config == null)
            {
                return Results.NotFound();
            }

            var dto = new RemoteServerPropertiesDto
            {
                Motd = config.Motd ?? string.Empty,
                Gamemode = config.Gamemode ?? "survival",
                Difficulty = config.Difficulty ?? "easy",
                MaxPlayers = int.TryParse(config.MaxPlayers, out int mp) ? mp : 20,
                Pvp = config.Pvp,
                Whitelist = config.WhiteList,
                AllowFlight = config.AllowFlight,
                AllowCommandBlock = config.AllowCommandBlock,
                AllowNether = config.AllowNether,
                ViewDistance = config.ViewDistance ?? "10",
                Seed = config.Seed ?? string.Empty
            };

            return Results.Ok(dto);
        });

        api.MapPut("/instances/{instanceId:guid}/properties", async (HttpContext context, Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteServerSettings)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var metadata = _instanceRegistry?.GetById(instanceId);
            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (metadata == null || string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
            {
                return Results.NotFound();
            }

            var request = await ReadJsonAsync<RemoteServerPropertiesDto>(context);
            if (request == null)
            {
                return Results.BadRequest(new { error = "Invalid properties payload" });
            }

            var config = _serverConfigurationService?.Load(metadata, serverDir) ?? new ServerConfiguration();
            config.Motd = request.Motd ?? config.Motd;
            config.Gamemode = request.Gamemode ?? config.Gamemode;
            config.Difficulty = request.Difficulty ?? config.Difficulty;
            config.MaxPlayers = request.MaxPlayers > 0 ? request.MaxPlayers.ToString() : config.MaxPlayers;
            config.Pvp = request.Pvp;
            config.WhiteList = request.Whitelist;
            config.AllowFlight = request.AllowFlight;
            config.AllowCommandBlock = request.AllowCommandBlock;
            config.AllowNether = request.AllowNether;
            if (!string.IsNullOrWhiteSpace(request.ViewDistance)) config.ViewDistance = request.ViewDistance;
            if (!string.IsNullOrWhiteSpace(request.Seed)) config.Seed = request.Seed;

            _serverConfigurationService?.Save(metadata, serverDir, config);
            _auditLogService.Log("remote", "instance.properties_update", instanceId);

            return Results.Ok(new { success = true });
        });

        api.MapGet("/instances/{instanceId:guid}/addons", (Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteServerAddons)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
            {
                return Results.NotFound();
            }

            var addons = GetInstalledAddonsForInstance(serverDir);
            return Results.Ok(addons);
        });

        api.MapPost("/instances/{instanceId:guid}/addons/uninstall", async (HttpContext context, Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteServerAddons)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
            {
                return Results.NotFound();
            }

            var request = await ReadJsonAsync<RemoteUninstallAddonRequest>(context);
            if (string.IsNullOrWhiteSpace(request?.AddonPathOrId))
            {
                return Results.BadRequest(new { error = "Addon path is required." });
            }

            string fullServerDir = Path.GetFullPath(serverDir);
            string targetPath = Path.GetFullPath(Path.Combine(serverDir, request.AddonPathOrId.TrimStart('/', '\\')));

            if (!targetPath.StartsWith(fullServerDir, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Path is outside instance folder." });
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                _auditLogService.Log("remote", "addon.uninstall", instanceId, request.AddonPathOrId);
                return Results.Ok(new { success = true });
            }
            else if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
                _auditLogService.Log("remote", "addon.uninstall", instanceId, request.AddonPathOrId);
                return Results.Ok(new { success = true });
            }

            return Results.NotFound(new { error = "Addon target not found." });
        });

        // ---------------------------------------------------------
        // File Manager Endpoints
        // ---------------------------------------------------------
        api.MapGet("/instances/{instanceId:guid}/files", (Guid instanceId, string? path) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteFileManager)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir)) return Results.NotFound(new { error = "Instance folder not found" });

            string? targetPath = GetSanitizedPath(serverDir, path ?? string.Empty);
            if (targetPath == null || (!Directory.Exists(targetPath) && !File.Exists(targetPath)))
            {
                return Results.BadRequest(new { error = "Invalid directory or file path." });
            }

            if (File.Exists(targetPath))
            {
                var fi = new FileInfo(targetPath);
                return Results.Ok(new List<RemoteFileItemDto>
                {
                    new RemoteFileItemDto
                    {
                        Name = fi.Name,
                        RelativePath = Path.GetRelativePath(serverDir, fi.FullName).Replace('\\', '/'),
                        IsDirectory = false,
                        SizeBytes = fi.Length,
                        LastModified = fi.LastWriteTimeUtc,
                        Extension = fi.Extension.ToLowerInvariant()
                    }
                });
            }

            var items = new List<RemoteFileItemDto>();
            var dirInfo = new DirectoryInfo(targetPath);

            foreach (var dir in dirInfo.GetDirectories())
            {
                if (dir.Name.StartsWith(".") && dir.Name != ".pocket-mc.json") continue;
                items.Add(new RemoteFileItemDto
                {
                    Name = dir.Name,
                    RelativePath = Path.GetRelativePath(serverDir, dir.FullName).Replace('\\', '/'),
                    IsDirectory = true,
                    SizeBytes = 0,
                    LastModified = dir.LastWriteTimeUtc,
                    Extension = string.Empty
                });
            }

            foreach (var file in dirInfo.GetFiles())
            {
                items.Add(new RemoteFileItemDto
                {
                    Name = file.Name,
                    RelativePath = Path.GetRelativePath(serverDir, file.FullName).Replace('\\', '/'),
                    IsDirectory = false,
                    SizeBytes = file.Length,
                    LastModified = file.LastWriteTimeUtc,
                    Extension = file.Extension.ToLowerInvariant()
                });
            }

            return Results.Ok(items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase));
        });

        api.MapGet("/instances/{instanceId:guid}/files/content", (Guid instanceId, string? path) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteFileManager)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir)) return Results.NotFound(new { error = "Instance folder not found" });

            string? targetPath = GetSanitizedPath(serverDir, path ?? string.Empty);
            if (targetPath == null || !File.Exists(targetPath)) return Results.BadRequest(new { error = "File not found." });

            var fi = new FileInfo(targetPath);
            if (fi.Length > 1 * 1024 * 1024)
            {
                return Results.Ok(new RemoteFileContentDto
                {
                    RelativePath = Path.GetRelativePath(serverDir, fi.FullName).Replace('\\', '/'),
                    Content = "[File exceeds 1 MB limit for browser editing]",
                    IsText = true,
                    IsTruncated = true,
                    SizeBytes = fi.Length
                });
            }

            string ext = fi.Extension.ToLowerInvariant();
            var binaryExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                ".jar", ".zip", ".tar", ".gz", ".7z", ".rar",
                ".png", ".jpg", ".jpeg", ".gif", ".ico",
                ".dat", ".dat_old", ".mca", ".nbt", ".lock",
                ".exe", ".dll", ".so", ".dylib", ".bin", ".db", ".sqlite", ".phar"
            };
            if (binaryExts.Contains(ext))
            {
                return Results.Ok(new RemoteFileContentDto
                {
                    RelativePath = Path.GetRelativePath(serverDir, fi.FullName).Replace('\\', '/'),
                    Content = "[Binary file cannot be viewed in text editor]",
                    IsText = false,
                    SizeBytes = fi.Length
                });
            }

            string text = File.ReadAllText(targetPath);
            return Results.Ok(new RemoteFileContentDto
            {
                RelativePath = Path.GetRelativePath(serverDir, fi.FullName).Replace('\\', '/'),
                Content = text,
                IsText = true,
                SizeBytes = fi.Length
            });
        });

        api.MapPut("/instances/{instanceId:guid}/files/content", async (HttpContext context, Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteFileManager)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir)) return Results.NotFound(new { error = "Instance folder not found" });

            var req = await ReadJsonAsync<SaveRemoteFileContentRequest>(context);
            if (req == null || string.IsNullOrWhiteSpace(req.RelativePath)) return Results.BadRequest(new { error = "RelativePath is required." });

            string? targetPath = GetSanitizedPath(serverDir, req.RelativePath);
            if (targetPath == null) return Results.BadRequest(new { error = "Invalid file path." });

            await File.WriteAllTextAsync(targetPath, req.Content ?? string.Empty);
            _auditLogService.Log("remote", "file.save", instanceId, req.RelativePath);
            return Results.Ok(new { success = true });
        });

        api.MapDelete("/instances/{instanceId:guid}/files", (Guid instanceId, string? path) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteFileManager)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir)) return Results.NotFound(new { error = "Instance folder not found" });

            if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { error = "Path is required." });

            string? targetPath = GetSanitizedPath(serverDir, path);
            if (targetPath == null || string.Equals(targetPath, Path.GetFullPath(serverDir), StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Cannot delete root instance folder." });
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                _auditLogService.Log("remote", "file.delete", instanceId, path);
                return Results.Ok(new { success = true });
            }
            else if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
                _auditLogService.Log("remote", "file.delete_dir", instanceId, path);
                return Results.Ok(new { success = true });
            }

            return Results.NotFound(new { error = "File or folder not found." });
        });

        // ---------------------------------------------------------
        // Backups Endpoints
        // ---------------------------------------------------------
        api.MapGet("/instances/{instanceId:guid}/backups", (Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteServerBackups)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            var metadata = _instanceRegistry?.GetById(instanceId);
            if (string.IsNullOrEmpty(serverDir) || metadata == null || !Directory.Exists(serverDir)) return Results.NotFound(new { error = "Instance folder not found" });

            var list = new List<RemoteBackupDto>();
            var defaultDir = Path.Combine(serverDir, "backups");
            var customDir = metadata.CustomBackupDirectory;

            var directoriesToScan = new List<string> { defaultDir };
            if (!string.IsNullOrWhiteSpace(customDir) && customDir != defaultDir && Directory.Exists(customDir))
            {
                directoriesToScan.Add(customDir);
            }

            var zipFiles = new List<string>();
            foreach (var dir in directoriesToScan)
            {
                if (Directory.Exists(dir))
                {
                    try { zipFiles.AddRange(Directory.GetFiles(dir, "*.zip")); } catch { }
                }
            }

            var uniqueZips = zipFiles
                .Select(f => new FileInfo(f))
                .GroupBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(fi => !string.IsNullOrWhiteSpace(customDir) && fi.FullName.StartsWith(customDir, StringComparison.OrdinalIgnoreCase)).First())
                .OrderByDescending(fi => fi.CreationTime)
                .ToList();

            var manifest = PocketMC.Domain.Models.BackupManifest.Load(serverDir);
            bool isRunning = _activeBackups.TryGetValue(instanceId, out bool r) && r;

            foreach (var fi in uniqueZips)
            {
                var metaEntry = manifest.Entries.FirstOrDefault(e =>
                    string.Equals(e.FileName, fi.Name, StringComparison.OrdinalIgnoreCase));

                if (isRunning && metaEntry == null && (DateTime.Now - fi.CreationTime).TotalMinutes < 5)
                {
                    continue; // Skip the partial backup file currently being generated
                }

                bool isDefault = fi.FullName.StartsWith(defaultDir, StringComparison.OrdinalIgnoreCase);

                var dto = new RemoteBackupDto
                {
                    Id = fi.Name,
                    FileName = fi.Name,
                    SizeBytes = fi.Length,
                    CreatedAt = fi.CreationTime,
                    Type = isDefault ? "Local" : "Custom",
                    IsAutomated = fi.Name.Contains("auto", StringComparison.OrdinalIgnoreCase)
                };

                if (metaEntry != null)
                {
                    dto.Version = metaEntry.Version.ToString();
                    dto.TriggerText = metaEntry.Trigger == PocketMC.Domain.Models.BackupTrigger.Manual ? "Manual" : "Scheduled";
                    dto.Label = metaEntry.Label ?? string.Empty;
                    dto.ServerVersion = metaEntry.MinecraftVersion;
                    dto.ServerType = metaEntry.ServerType;
                    dto.HasChecksum = metaEntry.Sha256Checksum != null;
                    dto.IntegrityVerified = metaEntry.IntegrityVerified;
                    dto.SizeDeltaBytes = metaEntry.SizeDeltaBytes;
                }

                list.Add(dto);
            }

            return Results.Ok(new { isBackupRunning = isRunning, backups = list });
        });

        api.MapPost("/instances/{instanceId:guid}/backups", (Guid instanceId) =>
        {
            if (!_applicationState.Settings.RemoteControl.AllowRemoteServerBackups)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var serverDir = _instanceRegistry?.GetPath(instanceId);
            var metadata = _instanceRegistry?.GetById(instanceId);
            if (string.IsNullOrEmpty(serverDir) || metadata == null || !Directory.Exists(serverDir)) return Results.NotFound(new { error = "Instance folder not found" });
            
            if (_backupService == null) return Results.BadRequest(new { error = "Backup service is unavailable" });

            _auditLogService.Log("remote", "backup.create", instanceId, "Background Backup");
            
            _activeBackups[instanceId] = true;
            _ = Task.Run(async () => 
            {
                try
                {
                    await _backupService.RunBackupAsync(metadata, serverDir, isManualBackup: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background backup failed for instance {InstanceId}", instanceId);
                }
                finally
                {
                    _activeBackups.TryRemove(instanceId, out _);
                }
            });

            return Results.Ok(new { success = true, fileName = "Started in background" });
        });

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
            AllowRemotePlayerActions = settings.AllowRemotePlayerActions,
            AllowRemoteServerSettings = settings.AllowRemoteServerSettings,
            AllowRemoteServerAddons = settings.AllowRemoteServerAddons,
            AllowRemoteFileManager = settings.AllowRemoteFileManager,
            AllowRemoteServerBackups = settings.AllowRemoteServerBackups
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

    private static List<RemoteAddonDto> GetInstalledAddonsForInstance(string serverDir)
    {
        var result = new List<RemoteAddonDto>();
        string[] subdirs = new[] { "plugins", "mods", "behavior_packs", "resource_packs" };

        foreach (var dirName in subdirs)
        {
            string dirPath = Path.Combine(serverDir, dirName);
            if (!Directory.Exists(dirPath)) continue;

            var dirInfo = new DirectoryInfo(dirPath);
            foreach (var file in dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly))
            {
                string ext = file.Extension.ToLowerInvariant();
                if (ext == ".jar" || ext == ".phar" || ext == ".mcpack" || ext == ".zip")
                {
                    string name = file.Name;
                    if (ext == ".jar")
                    {
                        try
                        {
                            var meta = JavaModMetadataService.ScanJar(file.FullName);
                            if (!string.IsNullOrWhiteSpace(meta?.DisplayName))
                            {
                                name = meta.DisplayName;
                            }
                        }
                        catch { }
                    }

                    string relPath = Path.Combine(dirName, file.Name).Replace('\\', '/');
                    result.Add(new RemoteAddonDto
                    {
                        Name = name,
                        FilePath = relPath,
                        SizeKb = Math.Round(file.Length / 1024.0, 1),
                        LastModified = file.LastWriteTimeUtc.ToString("o"),
                        AddonType = ext == ".jar" || ext == ".phar" ? "plugin" : "pack"
                    });
                }
            }

            foreach (var subDir in dirInfo.GetDirectories())
            {
                string relPath = Path.Combine(dirName, subDir.Name).Replace('\\', '/');
                result.Add(new RemoteAddonDto
                {
                    Name = subDir.Name,
                    FilePath = relPath,
                    SizeKb = 0,
                    LastModified = subDir.LastWriteTimeUtc.ToString("o"),
                    AddonType = "pack"
                });
            }
        }

        return result;
    }

    private static string? GetSanitizedPath(string baseDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir)) return null;
        string fullBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string combined = Path.GetFullPath(Path.Combine(fullBase, (relativePath ?? string.Empty).TrimStart('/', '\\')));
        if (!combined.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, fullBase, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return combined;
    }
}

