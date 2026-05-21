using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Infrastructure.Security;
using PocketMC.Desktop.Infrastructure.FileSystem;

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

        public SettingsManager(string settingsFilePath, ILogger<SettingsManager>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(settingsFilePath))
            {
                throw new ArgumentException("Settings file path cannot be empty.", nameof(settingsFilePath));
            }

            _logger = logger;
            _settingsFilePath = settingsFilePath;
        }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return CreateDefaultSettings();
            }

            AppSettings? settings;
            try
            {
                var content = File.ReadAllText(_settingsFilePath);
                settings = JsonSerializer.Deserialize<AppSettings>(content);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load settings from {SettingsFilePath}. Falling back to defaults.", _settingsFilePath);
                return CreateDefaultSettings();
            }

            settings = Normalize(settings);
            UnprotectSecrets(settings);
            return settings;
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

            var originalCloudTokens = new System.Collections.Generic.Dictionary<string, CloudOAuthTokenSet>(StringComparer.OrdinalIgnoreCase);
            if (normalizedSettings.CloudTokens != null)
            {
                foreach (var kvp in normalizedSettings.CloudTokens)
                {
                    originalCloudTokens[kvp.Key] = new CloudOAuthTokenSet
                    {
                        Provider = kvp.Value.Provider,
                        AccessToken = kvp.Value.AccessToken,
                        RefreshToken = kvp.Value.RefreshToken,
                        ExpiresAtUtc = kvp.Value.ExpiresAtUtc,
                        Scope = kvp.Value.Scope,
                        TokenType = kvp.Value.TokenType,
                        AccountId = kvp.Value.AccountId
                    };
                }
            }

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

                if (normalizedSettings.CloudTokens != null)
                {
                    foreach (var kvp in normalizedSettings.CloudTokens)
                    {
                        var tokenSet = kvp.Value;
                        if (tokenSet != null)
                        {
                            if (!string.IsNullOrEmpty(tokenSet.AccessToken))
                            {
                                tokenSet.AccessToken = DataProtector.Protect(tokenSet.AccessToken);
                            }
                            if (!string.IsNullOrEmpty(tokenSet.RefreshToken))
                            {
                                tokenSet.RefreshToken = DataProtector.Protect(tokenSet.RefreshToken);
                            }
                        }
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
                if (normalizedSettings.CloudTokens != null)
                {
                    normalizedSettings.CloudTokens.Clear();
                    foreach (var kvp in originalCloudTokens)
                    {
                        normalizedSettings.CloudTokens[kvp.Key] = kvp.Value;
                    }
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
            settings.AiApiKeys ??= new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            settings.CloudTokens ??= new System.Collections.Generic.Dictionary<string, CloudOAuthTokenSet>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new System.Collections.Generic.List<string>(settings.CloudTokens.Keys))
            {
                if (settings.CloudTokens[key] == null)
                {
                    settings.CloudTokens.Remove(key);
                }
            }
            settings.UserRemovedJavaVersions ??= new System.Collections.Generic.HashSet<int>();
            settings.CloudBackups ??= new CloudBackupSettings();
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

        private void UnprotectSecrets(AppSettings settings)
        {
            settings.CurseForgeApiKey = TryUnprotectSetting(settings.CurseForgeApiKey, nameof(settings.CurseForgeApiKey));

            if (settings.PlayitPartnerConnection != null)
            {
                settings.PlayitPartnerConnection.AgentSecretKey = TryUnprotectSetting(
                    settings.PlayitPartnerConnection.AgentSecretKey,
                    $"{nameof(settings.PlayitPartnerConnection)}.{nameof(settings.PlayitPartnerConnection.AgentSecretKey)}");
            }

            foreach (var key in new System.Collections.Generic.List<string>(settings.AiApiKeys.Keys))
            {
                string? unprotected = TryUnprotectSetting(settings.AiApiKeys[key], $"AiApiKeys.{key}");
                if (unprotected == null && !string.IsNullOrEmpty(settings.AiApiKeys[key]))
                {
                    settings.AiApiKeys.Remove(key);
                    continue;
                }

                settings.AiApiKeys[key] = unprotected ?? string.Empty;
            }

            foreach (var key in new System.Collections.Generic.List<string>(settings.CloudTokens.Keys))
            {
                var tokenSet = settings.CloudTokens[key];
                if (tokenSet == null || !TryUnprotectCloudTokenSet(tokenSet, key))
                {
                    settings.CloudTokens.Remove(key);
                }
            }
        }

        private string? TryUnprotectSetting(string? value, string settingName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            try
            {
                return DataProtector.Unprotect(value);
            }
            catch (CryptographicException ex)
            {
                _logger?.LogWarning(ex, "Failed to decrypt protected setting {SettingName}. Clearing only that value.", settingName);
                return null;
            }
        }

        private bool TryUnprotectCloudTokenSet(CloudOAuthTokenSet tokenSet, string providerName)
        {
            if (!TryUnprotectCloudToken(tokenSet.AccessToken, $"{nameof(AppSettings.CloudTokens)}.{providerName}.{nameof(tokenSet.AccessToken)}", out var accessToken))
            {
                return false;
            }

            if (!TryUnprotectCloudToken(tokenSet.RefreshToken, $"{nameof(AppSettings.CloudTokens)}.{providerName}.{nameof(tokenSet.RefreshToken)}", out var refreshToken))
            {
                return false;
            }

            tokenSet.AccessToken = accessToken;
            tokenSet.RefreshToken = refreshToken;
            return true;
        }

        private bool TryUnprotectCloudToken(string? value, string settingName, out string? unprotected)
        {
            unprotected = value;
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            try
            {
                unprotected = DataProtector.Unprotect(value);
                return true;
            }
            catch (CryptographicException ex)
            {
                _logger?.LogWarning(ex, "Failed to decrypt protected setting {SettingName}. Removing that cloud token provider.", settingName);
                unprotected = null;
                return false;
            }
        }
    }
}
