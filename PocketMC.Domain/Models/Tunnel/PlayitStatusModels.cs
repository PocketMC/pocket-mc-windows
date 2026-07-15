using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PocketMC.Domain.Models.Tunnel
{
    public class PlayitStatusResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("data")]
        public List<PlayitStatusMonitor>? Data { get; set; }
    }

    public class PlayitStatusMonitor
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("statusClass")]
        public string? StatusClass { get; set; }
    }
}
