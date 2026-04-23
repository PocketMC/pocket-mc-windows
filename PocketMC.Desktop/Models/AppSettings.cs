using System;

namespace PocketMC.Desktop.Models
{
    public class PlayitPartnerConnection
    {
        public string? AgentId { get; set; }
        public string? AgentSecretKey { get; set; }
        public long? AccountId { get; set; }
        public string? ConnectedEmail { get; set; }
        public string? Platform { get; set; }
        public string? AgentVersion { get; set; }
        public DateTimeOffset? ConnectedAtUtc { get; set; }
    }

    public class AppSettings
    {
        public string? AppRootPath { get; set; }
        public string? PlayitConfigDirectory { get; set; }
        public PlayitPartnerConnection? PlayitPartnerConnection { get; set; }
        public bool HasCompletedFirstLaunch { get; set; }
        public bool EnableMicaEffect { get; set; } = true;
        public string ApplicationTheme { get; set; } = "System";
        public string? CurseForgeApiKey { get; set; }

        // AI Summarization
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? AiApiKey { get; set; }
        public System.Collections.Generic.Dictionary<string, string> AiApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool EnableAiSummarization { get; set; } = false;
        public string AiProvider { get; set; } = "Gemini";
        public bool AlwaysAutoSummarize { get; set; } = false;


        public string? GetCurrentAiKey() => AiApiKeys.TryGetValue(AiProvider, out var key) ? key : null;
        
        // Disaster Recovery
        public string? ExternalBackupDirectory { get; set; }

        // Console Settings
        public int ConsoleBufferSize { get; set; } = 5000;

        // User Intent Flags
        public System.Collections.Generic.HashSet<int> UserRemovedJavaVersions { get; set; } = new();
    }
}
