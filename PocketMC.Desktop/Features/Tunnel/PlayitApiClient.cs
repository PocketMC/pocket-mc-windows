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
    }

    public class TunnelListResult
    {
        public bool Success { get; set; }
        public List<TunnelData> Tunnels { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public bool IsTokenInvalid { get; set; }
        public bool RequiresClaim { get; set; }
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
            if (!localPort.HasValue || string.IsNullOrWhiteSpace(publicAddress))
            {
                return null;
            }

            return new TunnelData
            {
                Id = tunnel.Id,
                Name = tunnel.Name,
                Port = localPort.Value,
                PublicAddress = publicAddress,
                NumericAddress = ExtractNumericAddress(tunnel),
                TunnelType = tunnel.TunnelType,
                Protocol = InferProtocol(tunnel.TunnelType)
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
    }
}
