using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Features.Instances.Backups;
using PocketMC.Desktop.Features.Setup;
using PocketMC.Desktop.Features.Console;
using PocketMC.Desktop.Infrastructure.Process;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Infrastructure.FileSystem;
using PocketMC.Desktop.Features.Settings;
using PocketMC.Desktop.Core.Presentation;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsManager
    {
        private readonly string _settingsFilePath;
        private readonly ILogger<SettingsManager>? _logger;

        public SettingsManager(ILogger<SettingsManager>? logger = null)
        {
            _logger = logger;
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketMC",
                "settings.json");
        }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return CreateDefaultSettings();
            }

            try
            {
                var content = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(content);
                if (settings != null)
                {
                    if (!string.IsNullOrEmpty(settings.CurseForgeApiKey))
                    {
                        settings.CurseForgeApiKey = DataProtector.Unprotect(settings.CurseForgeApiKey);
                    }

                    if (!string.IsNullOrEmpty(settings.PlayitPartnerConnection?.AgentSecretKey))
                    {
                        settings.PlayitPartnerConnection.AgentSecretKey =
                            DataProtector.Unprotect(settings.PlayitPartnerConnection.AgentSecretKey);
                    }

                    if (settings.AiApiKeys != null)
                    {
                        foreach (var key in new System.Collections.Generic.List<string>(settings.AiApiKeys.Keys))
                        {
                            if (!string.IsNullOrEmpty(settings.AiApiKeys[key]))
                            {
                                settings.AiApiKeys[key] = DataProtector.Unprotect(settings.AiApiKeys[key]);
                            }
                        }
                    }
                }
                return Normalize(settings);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load settings from {SettingsFilePath}. Falling back to defaults.", _settingsFilePath);
                return CreateDefaultSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            var normalizedSettings = Normalize(settings);
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var originalCurseForgeKey = normalizedSettings.CurseForgeApiKey;
            var originalPlayitSecret = normalizedSettings.PlayitPartnerConnection?.AgentSecretKey;
            var originalAiApiKeys = new System.Collections.Generic.Dictionary<string, string>(normalizedSettings.AiApiKeys, StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!string.IsNullOrEmpty(normalizedSettings.CurseForgeApiKey))
                {
                    normalizedSettings.CurseForgeApiKey = DataProtector.Protect(normalizedSettings.CurseForgeApiKey);
                }

                if (!string.IsNullOrEmpty(normalizedSettings.PlayitPartnerConnection?.AgentSecretKey))
                {
                    normalizedSettings.PlayitPartnerConnection.AgentSecretKey =
                        DataProtector.Protect(normalizedSettings.PlayitPartnerConnection.AgentSecretKey);
                }

                foreach (var kvp in originalAiApiKeys)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        normalizedSettings.AiApiKeys[kvp.Key] = DataProtector.Protect(kvp.Value);
                    }
                }

                var content = JsonSerializer.Serialize(normalizedSettings, new JsonSerializerOptions { WriteIndented = true });
                FileUtils.AtomicWriteAllText(_settingsFilePath, content);
            }
            finally
            {
                normalizedSettings.CurseForgeApiKey = originalCurseForgeKey;
                if (normalizedSettings.PlayitPartnerConnection != null)
                {
                    normalizedSettings.PlayitPartnerConnection.AgentSecretKey = originalPlayitSecret;
                }
                
                normalizedSettings.AiApiKeys.Clear();
                foreach (var kvp in originalAiApiKeys)
                {
                    normalizedSettings.AiApiKeys[kvp.Key] = kvp.Value;
                }
            }
        }

        public string GetPlayitTomlPath(AppSettings? settings = null)
        {
            var effectiveSettings = Normalize(settings ?? Load());
            return Path.Combine(effectiveSettings.PlayitConfigDirectory!, "playit.toml");
        }

        private const string PlayitPartnerBackendUrl = "https://pocket-mc-proxy.onrender.com";

        public string GetPlayitPartnerBackendUrl(AppSettings? settings = null)
        {
            // Dev override only — never exposed to users
            string? fromEnvironment = Environment.GetEnvironmentVariable("POCKETMC_PLAYIT_BACKEND_URL");
            return !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment : PlayitPartnerBackendUrl;
        }

        private AppSettings CreateDefaultSettings()
        {
            return Normalize(new AppSettings());
        }

        private AppSettings Normalize(AppSettings? settings)
        {
            settings ??= new AppSettings();
            settings.PlayitConfigDirectory ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "playit_gg");

            // Migration: Move old single API key to the dictionary under Gemini
            if (!string.IsNullOrEmpty(settings.AiApiKey))
            {
                if (!settings.AiApiKeys.ContainsKey("Gemini"))
                    settings.AiApiKeys["Gemini"] = settings.AiApiKey;

                settings.AiApiKey = null; // Clear it out so it stops writing to JSON
            }

            return settings;
        }
    }
}
