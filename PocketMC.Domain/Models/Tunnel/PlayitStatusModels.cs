using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PocketMC.Domain.Models.Tunnel
{
    public class PlayitStatusResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("generatedAt")]
        public string? GeneratedAt { get; set; }

        [JsonPropertyName("networks")]
        public List<PlayitStatusNetwork>? Networks { get; set; }

        [JsonPropertyName("data")]
        public List<PlayitStatusMonitor>? Data { get; set; }
    }

    public class PlayitStatusNetwork
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("uptime")]
        public double? Uptime { get; set; }
    }

    public class PlayitStatusMonitor
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("statusClass")]
        public string? StatusClass { get; set; }

        [JsonPropertyName("statusText")]
        public string? StatusText { get; set; }
    }
}
