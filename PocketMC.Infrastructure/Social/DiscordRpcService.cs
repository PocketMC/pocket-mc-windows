using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Instances;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using DiscordRPC;
using DiscordRPC.Logging;
using PocketMC.Application.Interfaces;

using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Instances;

/// <summary>
/// Background singleton service that manages Discord Rich Presence status.
/// Shows the active Minecraft server status on the user's Discord profile
/// including server type, player count, uptime, and a Join button.
/// </summary>
public sealed class DiscordRpcService : IDiscordRpcService
{
    private const string DiscordApplicationId = "1507742265240715354";

    private readonly IServerLifecycleService _lifecycleService;
    private readonly IResourceMonitorService _resourceMonitorService;
    private readonly ApplicationState _applicationState;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly ServerProcessManager _processManager;
    private readonly PocketMC.Application.Interfaces.Instances.IGeyserDetector _geyserDetector;
    private readonly ILogger<DiscordRpcService> _logger;

    private DiscordRpcClient? _client;
    private readonly object _lock = new();
    private bool _disposed;

    public DiscordRpcService(
        IServerLifecycleService lifecycleService,
        IResourceMonitorService resourceMonitorService,
        ApplicationState applicationState,
        InstanceRegistry instanceRegistry,
        ServerProcessManager processManager,
        PocketMC.Application.Interfaces.Instances.IGeyserDetector geyserDetector,
        ILogger<DiscordRpcService> logger)
    {
        _lifecycleService = lifecycleService;
        _resourceMonitorService = resourceMonitorService;
        _applicationState = applicationState;
        _instanceRegistry = instanceRegistry;
        _processManager = processManager;
        _geyserDetector = geyserDetector;
        _logger = logger;
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_disposed) return;

            if (!_applicationState.Settings.EnableDiscordRpc)
            {
                _logger.LogInformation("Discord RPC is disabled in settings. Skipping initialization.");
                return;
            }

            if (_client != null && !_client.IsDisposed)
            {
                _logger.LogDebug("Discord RPC client is already initialized.");
                return;
            }

            try
            {
                _client = new DiscordRpcClient(DiscordApplicationId)
                {
                    Logger = new DiscordToMelLogger(_logger)
                };

                _client.OnReady += (_, e) =>
                {
                    _logger.LogInformation("Discord RPC connected for user {Username}.",
                        e.User.Username);
                    UpdatePresence();
                };

                _client.OnConnectionFailed += (_, e) =>
                {
                    _logger.LogDebug("Discord RPC connection failed (pipe {Pipe}). Discord may not be running.",
                        e.FailedPipe);
                };

                _client.OnError += (_, e) =>
                {
                    _logger.LogWarning("Discord RPC error: {Message}", e.Message);
                };

                _client.Initialize();

                // Subscribe to server state and metrics events for live updates
                _lifecycleService.OnInstanceStateChanged += OnServerStateChanged;
                _resourceMonitorService.InstanceMetricsUpdated += OnMetricsUpdated;

                _logger.LogInformation("Discord RPC service initialized.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Discord RPC client.");
                DisposeClient();
            }
        }
    }

    public void UpdatePresence()
    {
        lock (_lock)
        {
            if (_client == null || _client.IsDisposed || _disposed) return;

            var rpcSettings = _applicationState.Settings.DiscordRpc;
            if (!rpcSettings.Enabled)
            {
                _client.ClearPresence();
                return;
            }

            try
            {
                var presence = BuildPresence();
                if (presence == null)
                {
                    _client.ClearPresence();
                }
                else
                {
                    _client.SetPresence(presence);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update Discord RPC presence.");
            }
        }
    }

    public void Shutdown()
    {
        lock (_lock)
        {
            _lifecycleService.OnInstanceStateChanged -= OnServerStateChanged;
            _resourceMonitorService.InstanceMetricsUpdated -= OnMetricsUpdated;

            if (_client != null && !_client.IsDisposed)
            {
                try
                {
                    _client.ClearPresence();
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error during Discord RPC shutdown.");
                }
            }

            _client = null;
            _logger.LogInformation("Discord RPC service shut down.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void OnServerStateChanged(Guid instanceId, ServerState state)
    {
        UpdatePresence();
    }

    private void OnMetricsUpdated(object? sender, InstanceMetricsUpdatedEventArgs e)
    {
        UpdatePresence();
    }

    // ── Presence builder ────────────────────────────────────────────────

    private RichPresence? BuildPresence()
    {
        var rpcSettings = _applicationState.Settings.DiscordRpc;

        // Find the highest-priority running server
        var activeProcesses = _processManager.ActiveProcesses.Values
            .Where(p => p.State == ServerState.Online ||
                        p.State == ServerState.Starting ||
                        p.State == ServerState.Stopping)
            .ToList();

        if (activeProcesses.Count == 0)
        {
            return rpcSettings.ShowIdle ? BuildIdlePresence(rpcSettings) : null;
        }

        // Priority: Online > Starting > Stopping, then by player count desc, then by uptime desc
        var primary = activeProcesses
            .OrderBy(p => p.State switch
            {
                ServerState.Online => 0,
                ServerState.Starting => 1,
                ServerState.Stopping => 2,
                _ => 3
            })
            .ThenByDescending(p => p.PlayerCount)
            .First();

        var metadata = _instanceRegistry.GetById(primary.InstanceId);
        if (metadata == null)
        {
            return rpcSettings.ShowIdle ? BuildIdlePresence(rpcSettings) : null;
        }

        return BuildServerPresence(primary, metadata, rpcSettings);
    }

    private RichPresence BuildIdlePresence(DiscordRpcSettings rpcSettings)
    {
        var presence = new RichPresence
        {
            Details = "Managing Servers",
            State = "Idle",
            Assets = new Assets
            {
                LargeImageKey = "pocketmc",
                LargeImageText = $"{PocketMC.Infrastructure.Configuration.AppConfig.AppName} Companion App"
            }
        };

        if (rpcSettings.ShowDownloadButton)
        {
            presence.Buttons = new[]
            {
                new Button { Label = $"Download {PocketMC.Infrastructure.Configuration.AppConfig.AppName}", Url = PocketMC.Infrastructure.Configuration.AppConfig.LinkWebsite }
            };
        }

        return presence;
    }

    private RichPresence BuildServerPresence(ServerProcess process, InstanceMetadata metadata, DiscordRpcSettings rpcSettings)
    {
        string engineKey = rpcSettings.ShowSoftwareIcon ? GetEngineAssetKey(metadata.ServerType) : "pocketmc";
        string engineLabel = metadata.ServerType;
        string serverName = rpcSettings.ShowServerName ? metadata.Name : "Minecraft Server";

        string details;
        string state;
        string? smallImageKey = rpcSettings.ShowSoftwareIcon ? "pocketmc" : null;
        string? smallImageText = null;

        switch (process.State)
        {
            case ServerState.Starting:
                details = $"Starting {serverName}";
                state = rpcSettings.ShowVersionAndEngine
                    ? $"{engineLabel} • {metadata.MinecraftVersion}"
                    : "Starting...";
                if (rpcSettings.ShowSoftwareIcon)
                {
                    smallImageText = "Starting...";
                }
                break;

            case ServerState.Stopping:
                details = $"Stopping {serverName}";
                state = rpcSettings.ShowVersionAndEngine
                    ? $"{engineLabel} • {metadata.MinecraftVersion}"
                    : "Stopping...";
                if (rpcSettings.ShowSoftwareIcon)
                {
                    smallImageText = "Stopping...";
                }
                break;

            case ServerState.Online:
            default:
                details = $"Hosting {serverName}";
                state = BuildOnlineStateText(process, metadata, rpcSettings);
                if (rpcSettings.ShowSoftwareIcon)
                {
                    smallImageText = rpcSettings.ShowVersionAndEngine
                        ? $"{engineLabel} {metadata.MinecraftVersion}"
                        : $"{PocketMC.Infrastructure.Configuration.AppConfig.AppName}";
                }
                break;
        }

        string largeTooltip;
        if (rpcSettings.ShowSoftwareIcon)
        {
            largeTooltip = rpcSettings.ShowVersionAndEngine
                ? $"{engineLabel} {metadata.MinecraftVersion}"
                : $"{engineLabel} Server";
        }
        else
        {
            largeTooltip = $"{PocketMC.Infrastructure.Configuration.AppConfig.AppName} Server";
        }

        var presence = new RichPresence
        {
            Details = TruncateForDiscord(details, 128),
            State = TruncateForDiscord(state, 128),
            Assets = new Assets
            {
                LargeImageKey = engineKey,
                LargeImageText = TruncateForDiscord(largeTooltip, 128),
                SmallImageKey = smallImageKey,
                SmallImageText = TruncateForDiscord(smallImageText, 128)
            }
        };

        // Add elapsed server uptime timer
        DateTime? sessionStart = _lifecycleService.GetSessionStartTime(process.InstanceId);
        if (sessionStart.HasValue)
        {
            presence.Timestamps = new Timestamps(sessionStart.Value.ToUniversalTime());
        }

        // Show Download PocketMC button if enabled
        if (rpcSettings.ShowDownloadButton)
        {
            presence.Buttons = new[]
            {
                new Button { Label = $"Download {PocketMC.Infrastructure.Configuration.AppConfig.AppName}", Url = PocketMC.Infrastructure.Configuration.AppConfig.LinkWebsite }
            };
        }

        return presence;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string BuildOnlineStateText(ServerProcess process, InstanceMetadata metadata, DiscordRpcSettings rpcSettings)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (rpcSettings.ShowPlayerCount)
        {
            int playerCount = process.PlayerCount;
            int maxPlayers = metadata.MaxPlayers;
            parts.Add($"{playerCount}/{maxPlayers} Players");
        }

        if (rpcSettings.ShowServerAddress)
        {
            bool isBedrock = metadata.ServerType.Equals("BedrockBDS", StringComparison.OrdinalIgnoreCase) ||
                             metadata.ServerType.Equals("Pocketmine", StringComparison.OrdinalIgnoreCase);

            if (isBedrock)
            {
                string? bedrockAddr = _applicationState.GetBedrockTunnelAddress(metadata.Id)
                    ?? _applicationState.GetTunnelAddress(metadata.Id);

                if (!string.IsNullOrEmpty(bedrockAddr))
                {
                    parts.Add(bedrockAddr);
                }
            }
            else if (_geyserDetector.IsGeyserInstalled(_instanceRegistry.GetPath(metadata.Id)))
            {
                string? javaAddr = _applicationState.GetTunnelAddress(metadata.Id);
                string? bedrockAddr = _applicationState.GetBedrockTunnelAddress(metadata.Id);

                if (!string.IsNullOrEmpty(javaAddr) && !string.IsNullOrEmpty(bedrockAddr))
                {
                    parts.Add($"Java: {javaAddr}");
                    parts.Add($"Bedrock: {bedrockAddr}");
                }
                else if (!string.IsNullOrEmpty(javaAddr))
                {
                    parts.Add(javaAddr);
                }
                else if (!string.IsNullOrEmpty(bedrockAddr))
                {
                    parts.Add($"Bedrock: {bedrockAddr}");
                }
            }
            else
            {
                string? normalAddr = _applicationState.GetTunnelAddress(metadata.Id);
                if (!string.IsNullOrEmpty(normalAddr))
                {
                    parts.Add(normalAddr);
                }
            }
        }
        else if (rpcSettings.ShowVersionAndEngine)
        {
            parts.Add($"{metadata.ServerType} {metadata.MinecraftVersion}");
        }

        if (parts.Count > 0)
        {
            return string.Join(" • ", parts);
        }

        return "Server Online";
    }

    private static string GetEngineAssetKey(string serverType)
    {
        if (string.IsNullOrEmpty(serverType)) return "pocketmc";
        string typeLower = serverType.ToLowerInvariant();
        if (typeLower.Contains("paper") || typeLower.Contains("purpur") || typeLower.Contains("spigot") || typeLower.Contains("bukkit")) return "paper";
        if (typeLower.Contains("fabric") || typeLower.Contains("quilt")) return "fabric";
        if (typeLower.Contains("neoforge")) return "neoforge";
        if (typeLower.Contains("forge")) return "forge";
        if (typeLower.Contains("bedrock") || typeLower.Contains("bds")) return "bedrock";
        if (typeLower.Contains("pocketmine")) return "pocketmine";
        if (typeLower.Contains("vanilla")) return "vanilla";
        return "pocketmc";
    }

    private static string? TruncateForDiscord(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        // Discord requires at least 2 characters for Details/State
        if (value.Length <= maxLength) return value.Length >= 2 ? value : value.PadRight(2);
        return value[..(maxLength - 1)] + "…";
    }

    private void DisposeClient()
    {
        try
        {
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose Discord RPC client.");
        }
        _client = null;
    }

    // ── Internal logging adapter ────────────────────────────────────────

    /// <summary>
    /// Bridges DiscordRPC's internal ILogger to Microsoft.Extensions.Logging.
    /// </summary>
    private sealed class DiscordToMelLogger : DiscordRPC.Logging.ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public DiscordToMelLogger(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }

        public DiscordRPC.Logging.LogLevel Level { get; set; } = DiscordRPC.Logging.LogLevel.Warning;

        public void Trace(string message, params object[] args)
        {
            _logger.LogTrace(message, args);
        }

        public void Info(string message, params object[] args)
        {
            _logger.LogDebug(message, args);
        }

        public void Warning(string message, params object[] args)
        {
            _logger.LogWarning(message, args);
        }

        public void Error(string message, params object[] args)
        {
            _logger.LogError(message, args);
        }
    }
}

