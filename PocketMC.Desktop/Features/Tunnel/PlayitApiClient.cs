using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Networking;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Tunnel
{
    public class TunnelData
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int Port { get; set; }
        public string PublicAddress { get; set; } = string.Empty;
        public string? NumericAddress { get; set; }
        public string? TunnelType { get; set; }
        public PortProtocol? Protocol { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool HasAgentOrigin { get; set; }
        public string? AgentId { get; set; }
        public string? LocalIp { get; set; }
        public bool HasPublicAddress => !string.IsNullOrEmpty(PublicAddress);
    }

    public class TunnelListResult
    {
        public bool Success { get; set; }
        public List<TunnelData> Tunnels { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public bool IsTokenInvalid { get; set; }
        public bool RequiresClaim { get; set; }
    }

    public class TunnelCreateResult
    {
        public bool Success { get; set; }
        public string? TunnelId { get; set; }
        public string? ErrorMessage { get; set; }
        /// <summary>
        /// The raw TunnelCreateErrorV1 string returned by the API (e.g. "RequiresVerifiedAccount").
        /// Populated only on a "fail" status response.
        /// </summary>
        public string? ErrorCode { get; set; }
        public bool IsTokenInvalid { get; set; }
        public bool RequiresClaim { get; set; }

        /// <summary>
        /// True when the API rejected the creation because the account's tunnel limit was reached.
        /// </summary>
        public bool IsLimitError =>
            ErrorCode is "RequiresPlayitPremium" or "RegionRequiresPlayitPremium";

        /// <summary>
        /// Maps a raw TunnelCreateErrorV1 code to a human-readable message for display in AppDialog.
        /// </summary>
        public static string MapCreateError(string? errorCode)
        {
            return errorCode switch
            {
                "RequiresPlayitPremium" => "Tunnel limit reached. Upgrade to PlayIt.gg Premium to create more tunnels.",
                "RegionRequiresPlayitPremium" => "The selected region requires a PlayIt.gg Premium account.",
                "PublicPortRequiresPlayitPremium" => "A public port requires PlayIt.gg Premium.",
                "RequiresVerifiedAccount" => "Your PlayIt.gg account must be verified before creating tunnels.",
                "AgentVersionTooOld" => "The PlayIt.gg agent is out of date. Please update it and try again.",
                "AgentNotFound" => "PlayIt.gg agent not found. Please reconnect and try again.",
                "TunnelNameIsNotAscii" => "Tunnel name contains invalid characters. Use ASCII characters only.",
                "TunnelNameTooLong" => "Tunnel name is too long. Please shorten it and try again.",
                "RegionNotSupported" => "The selected region is not supported for this tunnel type.",
                "TunnelTypeBlockedOnRegion" => "This tunnel type is not available in the selected region.",
                "GatewayAlreadyHasTunnelType" => "A tunnel of this type already exists on this gateway.",
                "InvalidTunnelConfig" => "Invalid tunnel configuration. Please check your settings and try again.",
                _ => $"Tunnel creation failed: {errorCode ?? "unknown error"}. Please try again."
            };
        }
    }

    /// <summary>
    /// Shared result type for tunnel management actions (rename, delete, enable, typeset, update).
    /// </summary>
    public class TunnelActionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public static TunnelActionResult Ok() => new() { Success = true };
        public static TunnelActionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    }

    internal sealed class PlayitApiEnvelope<TData>
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public TData? Data { get; set; }

        [JsonPropertyName("message")]
        public JsonElement Message { get; set; }
    }

    internal sealed class PlayitApiTunnelListV1
    {
        [JsonPropertyName("tunnels")]
        public List<PlayitAccountTunnelV1> Tunnels { get; set; } = new();
    }

    internal sealed class PlayitAccountTunnelV1
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tunnel_type")]
        public string? TunnelType { get; set; }

        [JsonPropertyName("user_enabled")]
        public bool UserEnabled { get; set; } = true;

        [JsonPropertyName("connect_addresses")]
        public List<PlayitConnectAddress> ConnectAddresses { get; set; } = new();

        [JsonPropertyName("origin")]
        public PlayitTunnelOriginV1? Origin { get; set; }

        [JsonPropertyName("public_allocations")]
        public List<PlayitPublicAllocation> PublicAllocations { get; set; } = new();
    }

    internal sealed class PlayitTunnelOriginV1
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public PlayitTunnelOriginDetails? Details { get; set; }
    }

    internal sealed class PlayitTunnelOriginDetails
    {
        [JsonPropertyName("agent_id")]
        public string? AgentId { get; set; }

        [JsonPropertyName("config_data")]
        public PlayitAgentTunnelConfig? ConfigData { get; set; }
    }

    internal sealed class PlayitAgentTunnelConfig
    {
        [JsonPropertyName("fields")]
        public List<PlayitAgentTunnelField> Fields { get; set; } = new();
    }

    internal sealed class PlayitAgentTunnelField
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    internal sealed class PlayitConnectAddress
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public JsonElement Value { get; set; }
    }

    internal sealed class PlayitPublicAllocation
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public PlayitPortAllocationDetails? Details { get; set; }
    }

    internal sealed class PlayitPortAllocationDetails
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }
    }

    public class PlayitApiClient
    {
        private const string BaseApiUrl = "https://api.playit.gg";
        private static readonly Regex SecretRegex = new(@"secret_key\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        private readonly HttpClient _httpClient;
        private readonly ApplicationState _applicationState;
        private readonly SettingsManager _settingsManager;
        private readonly ILogger<PlayitApiClient> _logger;

        public PlayitApiClient(ApplicationState applicationState, SettingsManager settingsManager, ILogger<PlayitApiClient> logger, HttpClient? httpClient = null)
        {
            _applicationState = applicationState;
            _settingsManager = settingsManager;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
            }

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        public PlayitPartnerConnection? GetPartnerConnection()
        {
            return _applicationState.Settings.PlayitPartnerConnection;
        }

        public string? GetAgentId()
        {
            return GetPartnerConnection()?.AgentId;
        }

        public bool HasPartnerConnection()
        {
            return !string.IsNullOrWhiteSpace(GetPartnerConnection()?.AgentSecretKey);
        }

        public string? GetSecretKey()
        {
            string? storedSecret = GetPartnerConnection()?.AgentSecretKey;
            if (!string.IsNullOrWhiteSpace(storedSecret))
            {
                return storedSecret;
            }

            string tomlPath = _settingsManager.GetPlayitTomlPath(_applicationState.Settings);
            if (!File.Exists(tomlPath))
            {
                return null;
            }

            try
            {
                string content = File.ReadAllText(tomlPath);
                Match match = SecretRegex.Match(content);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read playit secret.");
                return null;
            }
        }

        public async Task<TunnelListResult> GetTunnelsAsync()
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = "PocketMC is not connected to a Playit agent yet.",
                    RequiresClaim = true
                };
            }

            try
            {
                using HttpRequestMessage request = BuildAuthorizedRequest(HttpMethod.Post, "/v1/tunnels/list", secretKey, new { });
                using HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return new TunnelListResult
                    {
                        Success = false,
                        ErrorMessage = "The saved Playit credentials were rejected.",
                        IsTokenInvalid = true
                    };
                }

                response.EnsureSuccessStatusCode();
                PlayitApiEnvelope<PlayitApiTunnelListV1>? apiResponse =
                    JsonSerializer.Deserialize<PlayitApiEnvelope<PlayitApiTunnelListV1>>(await response.Content.ReadAsStringAsync());

                List<TunnelData> normalizedTunnels = apiResponse?.Data?.Tunnels?
                    .Select(NormalizeTunnel)
                    .Where(tunnel => tunnel != null)
                    .Cast<TunnelData>()
                    .ToList()
                    ?? new List<TunnelData>();

                return new TunnelListResult
                {
                    Success = true,
                    Tunnels = normalizedTunnels
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list Playit tunnels.");
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public static TunnelData? FindTunnelForRequest(IEnumerable<TunnelData> tunnels, PortCheckRequest request)
        {
            return tunnels.FirstOrDefault(t =>
                t.Port == request.Port &&
                (!t.Protocol.HasValue || ProtocolsOverlap(t.Protocol.Value, request.Protocol)));
        }

        /// <summary>
        /// Automatically creates a PlayIt tunnel via the v1/tunnels/create API.
        /// </summary>
        /// <param name="tunnelName">A human-readable name for the tunnel (e.g. "my-server-minecraft-java").</param>
        /// <param name="tunnelType">The PlayIt tunnel type: "minecraft-java" or "minecraft-bedrock".</param>
        /// <param name="localPort">The local server port to route traffic to.</param>
        /// <returns>A <see cref="TunnelCreateResult"/> indicating success/failure and the created tunnel ID.</returns>
        public async Task<TunnelCreateResult> CreateTunnelAsync(string tunnelName, string tunnelType, int localPort)
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return new TunnelCreateResult
                {
                    Success = false,
                    ErrorMessage = "PocketMC is not connected to a Playit agent yet.",
                    RequiresClaim = true
                };
            }

            try
            {
                var payload = new
                {
                    name = tunnelName,
                    protocol = new { type = "tunnel-type", details = tunnelType },
                    origin = new
                    {
                        type = "agent",
                        data = new
                        {
                            agent_id = (string?)null,
                            config = new
                            {
                                fields = new[]
                                {
                                    new { name = "local_port", value = localPort.ToString() }
                                }
                            }
                        }
                    },
                    endpoint = new
                    {
                        type = "region",
                        details = new { region = "global", port = (int?)null }
                    },
                    enabled = true
                };

                using HttpRequestMessage request = BuildAuthorizedRequest(HttpMethod.Post, "/v1/tunnels/create", secretKey, payload);
                using HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return new TunnelCreateResult
                    {
                        Success = false,
                        ErrorMessage = "The saved Playit credentials were rejected.",
                        IsTokenInvalid = true
                    };
                }

                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Playit tunnel creation failed with HTTP {StatusCode}: {Body}", (int)response.StatusCode, body);
                    return new TunnelCreateResult
                    {
                        Success = false,
                        ErrorMessage = $"Playit API returned HTTP {(int)response.StatusCode}."
                    };
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string status = root.TryGetProperty("status", out JsonElement statusEl) ? statusEl.GetString() ?? "" : "";

                if (status == "success" && root.TryGetProperty("data", out JsonElement data))
                {
                    string? createdId = data.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
                    return new TunnelCreateResult { Success = true, TunnelId = createdId };
                }

                if (status == "fail" && root.TryGetProperty("data", out JsonElement failData))
                {
                    string failMessage = failData.ValueKind == JsonValueKind.String
                        ? failData.GetString() ?? "Unknown error"
                        : failData.ToString();
                    return new TunnelCreateResult { Success = false, ErrorMessage = failMessage, ErrorCode = failMessage };
                }

                return new TunnelCreateResult { Success = false, ErrorMessage = $"Unexpected API response: {body}" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create Playit tunnel.");
                return new TunnelCreateResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Creates a PlayIt tunnel with the specified region and enabled state via v1/tunnels/create.
        /// </summary>
        public async Task<TunnelCreateResult> CreateTunnelAsync(string tunnelName, string tunnelType, int localPort, string region, bool enabled)
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return new TunnelCreateResult
                {
                    Success = false,
                    ErrorMessage = "PocketMC is not connected to a Playit agent yet.",
                    RequiresClaim = true
                };
            }

            try
            {
                var payload = new
                {
                    name = tunnelName,
                    protocol = new { type = "tunnel-type", details = tunnelType },
                    origin = new
                    {
                        type = "agent",
                        data = new
                        {
                            agent_id = (string?)null,
                            config = new
                            {
                                fields = new[]
                                {
                                    new { name = "local_port", value = localPort.ToString() }
                                }
                            }
                        }
                    },
                    endpoint = new
                    {
                        type = "region",
                        details = new { region, port = (int?)null }
                    },
                    enabled
                };

                using HttpRequestMessage request = BuildAuthorizedRequest(HttpMethod.Post, "/v1/tunnels/create", secretKey, payload);
                using HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return new TunnelCreateResult
                    {
                        Success = false,
                        ErrorMessage = "The saved Playit credentials were rejected.",
                        IsTokenInvalid = true
                    };
                }

                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Playit tunnel creation failed with HTTP {StatusCode}: {Body}", (int)response.StatusCode, body);
                    return new TunnelCreateResult
                    {
                        Success = false,
                        ErrorMessage = $"Playit API returned HTTP {(int)response.StatusCode}."
                    };
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string status = root.TryGetProperty("status", out JsonElement statusEl) ? statusEl.GetString() ?? "" : "";

                if (status == "success" && root.TryGetProperty("data", out JsonElement data))
                {
                    string? createdId = data.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
                    return new TunnelCreateResult { Success = true, TunnelId = createdId };
                }

                if (status == "fail" && root.TryGetProperty("data", out JsonElement failData))
                {
                    string failMessage = failData.ValueKind == JsonValueKind.String
                        ? failData.GetString() ?? "Unknown error"
                        : failData.ToString();
                    return new TunnelCreateResult { Success = false, ErrorMessage = failMessage, ErrorCode = failMessage };
                }

                return new TunnelCreateResult { Success = false, ErrorMessage = $"Unexpected API response: {body}" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create Playit tunnel.");
                return new TunnelCreateResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private HttpRequestMessage BuildAuthorizedRequest(HttpMethod method, string relativePath, string secretKey, object payload)
        {
            HttpRequestMessage request = new(method, new Uri(new Uri(BaseApiUrl), relativePath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Agent-Key", secretKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return request;
        }

        private static TunnelData? NormalizeTunnel(PlayitAccountTunnelV1 tunnel)
        {
            int? localPort = ExtractLocalPort(tunnel.Origin);
            string? publicAddress = ExtractPublicAddress(tunnel);

            // Allow tunnels without a resolved port (e.g. newly created / disabled)
            // but keep port = 0 as a signal they lack config data.
            return new TunnelData
            {
                Id = tunnel.Id,
                Name = tunnel.Name,
                Port = localPort ?? 0,
                PublicAddress = publicAddress ?? string.Empty,
                NumericAddress = ExtractNumericAddress(tunnel),
                TunnelType = tunnel.TunnelType,
                Protocol = InferProtocol(tunnel.TunnelType),
                IsEnabled = tunnel.UserEnabled,
                HasAgentOrigin = tunnel.Origin?.Type == "agent",
                AgentId = tunnel.Origin?.Details?.AgentId,
                LocalIp = ExtractLocalIp(tunnel.Origin)
            };
        }

        private static int? ExtractLocalPort(PlayitTunnelOriginV1? origin)
        {
            if (origin?.Details?.ConfigData?.Fields == null)
            {
                return null;
            }

            foreach (PlayitAgentTunnelField field in origin.Details.ConfigData.Fields)
            {
                if ((field.Name.Contains("port", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(field.Name, "local_port", StringComparison.OrdinalIgnoreCase)) &&
                    int.TryParse(field.Value, out int parsedPort))
                {
                    return parsedPort;
                }
            }

            return null;
        }

        private static string? ExtractLocalIp(PlayitTunnelOriginV1? origin)
        {
            if (origin?.Details?.ConfigData?.Fields == null)
            {
                return null;
            }

            foreach (PlayitAgentTunnelField field in origin.Details.ConfigData.Fields)
            {
                if (string.Equals(field.Name, "local_ip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field.Name, "local_address", StringComparison.OrdinalIgnoreCase))
                {
                    return field.Value;
                }
            }

            return null;
        }

        private static string? ExtractPublicAddress(PlayitAccountTunnelV1 tunnel)
        {
            foreach (PlayitConnectAddress address in tunnel.ConnectAddresses)
            {
                if (TryExtractDisplayAddress(address, out string? displayAddress) && !string.IsNullOrWhiteSpace(displayAddress))
                {
                    return displayAddress;
                }
            }

            PlayitPortAllocationDetails? allocation = tunnel.PublicAllocations
                .FirstOrDefault(x => x.Details != null)?.Details;
            if (!string.IsNullOrWhiteSpace(allocation?.Ip))
            {
                return $"{allocation.Ip}:{allocation.Port}";
            }

            return null;
        }

        private static string? ExtractNumericAddress(PlayitAccountTunnelV1 tunnel)
        {
            PlayitPortAllocationDetails? allocation = tunnel.PublicAllocations
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Details?.Ip))?.Details;
            return allocation == null ? null : $"{allocation.Ip}:{allocation.Port}";
        }

        private static bool TryExtractDisplayAddress(PlayitConnectAddress address, out string? displayAddress)
        {
            displayAddress = null;
            JsonElement value = address.Value;

            switch (address.Type)
            {
                case "domain":
                case "auto":
                    if (value.TryGetProperty("address", out JsonElement hostname))
                    {
                        displayAddress = hostname.GetString();
                    }
                    return true;

                case "addr4":
                case "addr6":
                    if (value.TryGetProperty("address", out JsonElement socketAddr))
                    {
                        displayAddress = socketAddr.GetString();
                    }
                    return true;

                case "ip4":
                case "ip6":
                    if (value.TryGetProperty("address", out JsonElement ip) &&
                        value.TryGetProperty("default_port", out JsonElement port))
                    {
                        displayAddress = $"{ip.GetString()}:{port.GetInt32()}";
                    }
                    return true;

                default:
                    return false;
            }
        }

        private static PortProtocol? InferProtocol(string? tunnelType)
        {
            if (string.IsNullOrWhiteSpace(tunnelType))
            {
                return null;
            }

            if (tunnelType.Contains("bedrock", StringComparison.OrdinalIgnoreCase) ||
                tunnelType.Contains("udp", StringComparison.OrdinalIgnoreCase))
            {
                return PortProtocol.Udp;
            }

            if (tunnelType.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                tunnelType.Contains("tcp", StringComparison.OrdinalIgnoreCase))
            {
                return PortProtocol.Tcp;
            }

            return null;
        }

        private static bool ProtocolsOverlap(PortProtocol left, PortProtocol right)
        {
            return left == PortProtocol.TcpAndUdp ||
                   right == PortProtocol.TcpAndUdp ||
                   left == right;
        }

        // ─── Tunnel management actions ────────────────────────────────────

        /// <summary>
        /// Sends an authorized POST to the given path and interprets the standard
        /// success / fail / error envelope. Returns <see cref="TunnelActionResult"/>.
        /// </summary>
        private async Task<TunnelActionResult> PostActionAsync(string path, object payload)
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return TunnelActionResult.Fail("PocketMC is not connected to a Playit agent.");
            }

            try
            {
                using HttpRequestMessage request = BuildAuthorizedRequest(HttpMethod.Post, path, secretKey, payload);
                using HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return TunnelActionResult.Fail("The saved Playit credentials were rejected.");
                }

                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Playit action {Path} failed with HTTP {StatusCode}: {Body}", path, (int)response.StatusCode, body);
                    return TunnelActionResult.Fail($"Playit API returned HTTP {(int)response.StatusCode}.");
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string status = root.TryGetProperty("status", out JsonElement statusEl) ? statusEl.GetString() ?? "" : "";

                if (status == "success")
                {
                    return TunnelActionResult.Ok();
                }

                if (status == "fail" && root.TryGetProperty("data", out JsonElement failData))
                {
                    string failMessage = failData.ValueKind == JsonValueKind.String
                        ? failData.GetString() ?? "Unknown error"
                        : failData.ToString();
                    return TunnelActionResult.Fail(failMessage);
                }

                return TunnelActionResult.Fail($"Unexpected API response: {body}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Playit action {Path} failed.", path);
                return TunnelActionResult.Fail(ex.Message);
            }
        }

        /// <summary>Renames a tunnel. Path: /tunnels/rename.</summary>
        public Task<TunnelActionResult> RenameTunnelAsync(string tunnelId, string newName)
            => PostActionAsync("/tunnels/rename", new { tunnel_id = tunnelId, name = newName });

        /// <summary>Deletes a tunnel. Path: /tunnels/delete.</summary>
        public Task<TunnelActionResult> DeleteTunnelAsync(string tunnelId)
            => PostActionAsync("/tunnels/delete", new { tunnel_id = tunnelId });

        /// <summary>Enables or disables a tunnel. Path: /tunnels/enable.</summary>
        public Task<TunnelActionResult> EnableTunnelAsync(string tunnelId, bool enabled)
            => PostActionAsync("/tunnels/enable", new { tunnel_id = tunnelId, enabled });

        /// <summary>Updates the local address / port / enabled state. Path: /tunnels/update.</summary>
        public Task<TunnelActionResult> UpdateTunnelAsync(string tunnelId, string localIp, int? localPort, string? agentId, bool enabled)
            => PostActionAsync("/tunnels/update", new { tunnel_id = tunnelId, local_ip = localIp, local_port = localPort, agent_id = agentId, enabled });
    }
}
