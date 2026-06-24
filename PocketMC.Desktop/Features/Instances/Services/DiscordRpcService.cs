using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using DiscordRPC;
using DiscordRPC.Logging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Instances.Services;

/// <summary>
/// Background singleton service that manages Discord Rich Presence status.
/// Shows the active Minecraft server status on the user's Discord profile
/// including server type, player count, uptime, and a Join button.
/// </summary>
public sealed class DiscordRpcService : IDiscordRpcService
{
    private const string DiscordApplicationId = "1507742265240715354";
    private const string PocketMcWebsiteUrl = "https://pocketmc.github.io/pocket-mc-website/";

    private readonly IServerLifecycleService _lifecycleService;
    private readonly IResourceMonitorService _resourceMonitorService;
    private readonly ApplicationState _applicationState;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly ServerProcessManager _processManager;
    private readonly PocketMC.Desktop.Helpers.IGeyserDetector _geyserDetector;
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
        PocketMC.Desktop.Helpers.IGeyserDetector geyserDetector,
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

            try
            {
                var presence = BuildPresence();
                _client.SetPresence(presence);
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

    private RichPresence BuildPresence()
    {
        // Find the highest-priority running server
        var activeProcesses = _processManager.ActiveProcesses.Values
            .Where(p => p.State == ServerState.Online ||
                        p.State == ServerState.Starting ||
                        p.State == ServerState.Stopping)
            .ToList();

        if (activeProcesses.Count == 0)
        {
            return BuildIdlePresence();
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
            return BuildIdlePresence();
        }

        return BuildServerPresence(primary, metadata);
    }

    private RichPresence BuildIdlePresence()
    {
        return new RichPresence
        {
            Details = "Managing Servers",
            State = "Idle",
            Assets = new Assets
            {
                LargeImageKey = "pocketmc",
                LargeImageText = "PocketMC Companion App"
            },
            Buttons = new[]
            {
                new Button { Label = "Download PocketMC", Url = PocketMcWebsiteUrl }
            }
        };
    }

    private RichPresence BuildServerPresence(ServerProcess process, InstanceMetadata metadata)
    {
        string engineKey = GetEngineAssetKey(metadata.ServerType);
        string engineLabel = metadata.ServerType;

        string details;
        string state;
        string smallImageKey;
        string smallImageText;

        switch (process.State)
        {
            case ServerState.Starting:
                details = $"Starting {metadata.Name}";
                state = $"{engineLabel} • {metadata.MinecraftVersion}";
                smallImageKey = "pocketmc";
                smallImageText = "Starting...";
                break;

            case ServerState.Stopping:
                details = $"Stopping {metadata.Name}";
                state = $"{engineLabel} • {metadata.MinecraftVersion}";
                smallImageKey = "pocketmc";
                smallImageText = "Stopping...";
                break;

            case ServerState.Online:
            default:
                details = $"Hosting {metadata.Name}";
                state = BuildOnlineStateText(process, metadata);

                smallImageKey = "pocketmc";
                smallImageText = $"{engineLabel} {metadata.MinecraftVersion}";
                break;
        }

        var presence = new RichPresence
        {
            Details = TruncateForDiscord(details, 128),
            State = TruncateForDiscord(state, 128),
            Assets = new Assets
            {
                LargeImageKey = engineKey,
                LargeImageText = $"{engineLabel} {metadata.MinecraftVersion}",
                SmallImageKey = smallImageKey,
                SmallImageText = smallImageText
            }
        };

        // Add elapsed timer if session start time is available
        DateTime? sessionStart = _lifecycleService.GetSessionStartTime(process.InstanceId);
        if (sessionStart.HasValue)
        {
            presence.Timestamps = new Timestamps(sessionStart.Value.ToUniversalTime());
        }

        // Always show Download PocketMC button
        presence.Buttons = new[]
        {
            new Button { Label = "Download PocketMC", Url = PocketMcWebsiteUrl }
        };

        return presence;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string BuildOnlineStateText(ServerProcess process, InstanceMetadata metadata)
    {
        int playerCount = process.PlayerCount;
        int maxPlayers = metadata.MaxPlayers;
        string prefix = $"{playerCount}/{maxPlayers} Players";

        bool isBedrock = metadata.ServerType.Equals("BedrockBDS", StringComparison.OrdinalIgnoreCase) ||
                         metadata.ServerType.Equals("Pocketmine", StringComparison.OrdinalIgnoreCase);

        if (isBedrock)
        {
            // Native Bedrock/Pocketmine: show bedrock tunnel address (includes external port)
            string? bedrockAddr = _applicationState.GetBedrockTunnelAddress(metadata.Id)
                ?? _applicationState.GetTunnelAddress(metadata.Id);

            if (!string.IsNullOrEmpty(bedrockAddr))
            {
                return $"{prefix} • {bedrockAddr}";
            }
            return $"{prefix} • Bedrock BDS";
        }

        if (_geyserDetector.IsGeyserInstalled(_instanceRegistry.GetPath(metadata.Id)))
        {
            // Java with Geyser: show Java address AND Bedrock/Geyser address
            string? javaAddr = _applicationState.GetTunnelAddress(metadata.Id);
            string? bedrockAddr = _applicationState.GetBedrockTunnelAddress(metadata.Id);

            if (!string.IsNullOrEmpty(javaAddr) && !string.IsNullOrEmpty(bedrockAddr))
            {
                return $"{prefix} • Java: {javaAddr} • Bedrock: {bedrockAddr}";
            }
            if (!string.IsNullOrEmpty(javaAddr))
            {
                return $"{prefix} • Java: {javaAddr}";
            }
            if (!string.IsNullOrEmpty(bedrockAddr))
            {
                return $"{prefix} • Bedrock: {bedrockAddr}";
            }

            return $"{prefix} • {metadata.ServerType} + Geyser";
        }

        // Standard Java server
        string? normalAddr = _applicationState.GetTunnelAddress(metadata.Id);
        if (!string.IsNullOrEmpty(normalAddr))
        {
            return $"{prefix} • {normalAddr}";
        }
        return $"{prefix} • {metadata.ServerType}";
    }

    private static string GetEngineAssetKey(string serverType)
    {
        if (string.IsNullOrEmpty(serverType)) return "pocketmc";
        string typeLower = serverType.ToLowerInvariant();
        if (typeLower.Contains("paper")) return "paper";
        if (typeLower.Contains("fabric")) return "fabric";
        if (typeLower.Contains("neoforge")) return "neoforge";
        if (typeLower.Contains("forge")) return "forge";
        if (typeLower.Contains("bedrock")) return "bedrock";
        if (typeLower.Contains("pocketmine")) return "pocketmine";
        if (typeLower.Contains("vanilla")) return "vanilla";
        return "pocketmc";
    }

    private static string TruncateForDiscord(string value, int maxLength)
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
        catch { }
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
