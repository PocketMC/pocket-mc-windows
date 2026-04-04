using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Services
{
    // --- Playit API Response Models ---

    public class PlayitApiResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public PlayitApiData? Data { get; set; }
    }

    public class PlayitApiData
    {
        [JsonPropertyName("tunnels")]
        public List<PlayitTunnelConfig> Tunnels { get; set; } = new();
    }

    public class PlayitTunnelConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tunnel_type")]
        public string? TunnelType { get; set; }

        [JsonPropertyName("alloc")]
        public PlayitAllocWrapper? Alloc { get; set; }

        [JsonPropertyName("origin")]
        public PlayitOriginWrapper? Origin { get; set; }
    }

    public class PlayitAllocWrapper
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public PlayitAllocData? Data { get; set; }
    }

    public class PlayitAllocData
    {
        [JsonPropertyName("ip_hostname")]
        public string IpHostname { get; set; } = string.Empty;

        [JsonPropertyName("port_start")]
        public int PortStart { get; set; }

        [JsonPropertyName("assigned_srv")]
        public string? AssignedSrv { get; set; }
    }

    public class PlayitOriginWrapper
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public PlayitOriginData? Data { get; set; }
    }

    public class PlayitOriginData
    {
        [JsonPropertyName("local_port")]
        public int LocalPort { get; set; }
    }

    // --- Custom UI Models ---

    /// <summary>
    /// Represents a normalized single Playit.gg tunnel entry.
    /// </summary>
    public class TunnelData
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int Port { get; set; } // Local port
        public string PublicAddress { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of a tunnel list API call, including error state.
    /// </summary>
    public class TunnelListResult
    {
        public bool Success { get; set; }
        public List<TunnelData> Tunnels { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public bool IsTokenInvalid { get; set; }
    }

    /// <summary>
    /// HTTP client for the Playit.gg tunnel API.
    /// Reads the agent secret from %APPDATA%/playit/playit.toml.
    /// Implements NET-05, NET-06, NET-09.
    /// </summary>
    public class PlayitApiClient
    {
        private readonly HttpClient _httpClient;
        private static readonly Regex SecretRegex = new(
            @"secret_key\s*=\s*""([^""]+)""",
            RegexOptions.Compiled);

        private const string TunnelApiUrl = "https://api.playit.gg/tunnels/list";

        public PlayitApiClient(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            // App needs a user agent and specific headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        /// <summary>
        /// Reads the agent secret key from %APPDATA%/playit/playit.toml (NET-05).
        /// Returns null if not found (agent never claimed).
        /// </summary>
        public string? GetSecretKey()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tomlPath = Path.Combine(appData, "playit_gg", "playit.toml");

            if (!File.Exists(tomlPath))
                return null;

            try
            {
                string content = File.ReadAllText(tomlPath);
                var match = SecretRegex.Match(content);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fetches the list of tunnels from the Playit.gg API (NET-06).
        /// Tunnel addresses are always resolved fresh — never cached (NET-09).
        /// </summary>
        public async Task<TunnelListResult> GetTunnelsAsync()
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrEmpty(secretKey))
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = "No agent secret key found. Agent may not be claimed yet.",
                    IsTokenInvalid = true
                };
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, TunnelApiUrl);
                // The correct Auth format is 'Agent-Key {secret}'
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Agent-Key", secretKey);

                // Endpoint requires an empty JSON body
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);

                // Detect token revocation (D-02 from CONTEXT.md)
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return new TunnelListResult
                    {
                        Success = false,
                        ErrorMessage = "Playit.gg token is invalid or revoked. Please re-link your account.",
                        IsTokenInvalid = true
                    };
                }

                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<PlayitApiResponse>(json);

                var normalizedTunnels = new List<TunnelData>();

                if (apiResponse?.Data?.Tunnels != null)
                {
                    foreach (var pt in apiResponse.Data.Tunnels)
                    {
                        if (pt.Alloc?.Data == null || pt.Origin?.Data == null) continue;

                        int localPort = pt.Origin.Data.LocalPort;
                        string publicAddress = !string.IsNullOrEmpty(pt.Alloc.Data.AssignedSrv) 
                            ? pt.Alloc.Data.AssignedSrv 
                            : $"{pt.Alloc.Data.IpHostname}:{pt.Alloc.Data.PortStart}";

                        normalizedTunnels.Add(new TunnelData
                        {
                            Id = pt.Id,
                            Name = pt.Name,
                            Port = localPort,
                            PublicAddress = publicAddress
                        });
                    }
                }

                return new TunnelListResult
                {
                    Success = true,
                    Tunnels = normalizedTunnels
                };
            }
            catch (HttpRequestException ex)
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = $"Could not verify tunnel status. Check your connection. ({ex.Message})"
                };
            }
            catch (Exception ex)
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = $"Unexpected error resolving tunnels: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Finds the tunnel entry whose port matches the given server port.
        /// Returns null if no match found.
        /// </summary>
        public static TunnelData? FindTunnelForPort(List<TunnelData> tunnels, int serverPort)
        {
            return tunnels.Find(t => t.Port == serverPort);
        }
    }
}
