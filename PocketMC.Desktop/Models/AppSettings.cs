using System;

namespace PocketMC.Desktop.Models
{
    public class AppSettings
    {
        public string? AppRootPath { get; set; }
        public string? PlayitConfigDirectory { get; set; }
        public bool HasCompletedFirstLaunch { get; set; }
        public bool EnableMicaEffect { get; set; } = true;
        public string? CurseForgeApiKey { get; set; }

        // AI Summarization
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? AiApiKey { get; set; }
        public System.Collections.Generic.Dictionary<string, string> AiApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool EnableAiSummarization { get; set; } = false;
        public string AiProvider { get; set; } = "Gemini";
        public bool AlwaysAutoSummarize { get; set; } = false;

        public string? GetCurrentAiKey() => AiApiKeys.TryGetValue(AiProvider, out var key) ? key : null;
    }
}
