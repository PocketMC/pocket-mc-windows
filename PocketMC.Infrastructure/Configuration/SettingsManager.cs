using PocketMC.Infrastructure.Configuration;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Models;
using PocketMC.Domain.Security;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Security;

namespace PocketMC.Infrastructure.Configuration
{
    public class SettingsManager
    {
        private static readonly JsonSerializerOptions SettingsJsonOptions = new() { WriteIndented = true };

        private readonly string _settingsFilePath;
        private readonly ILogger<SettingsManager>? _logger;
        private readonly object _settingsLock = new();
        private bool _settingsWritesBlocked;
        private bool _secretRecoveryBackupCreated;

        public event EventHandler<AppSettings>? SettingsSaved;

        public static string ResolveDefaultSettingsFilePath(string? customDataDirectory = null)
        {
            string baseDir = !string.IsNullOrWhiteSpace(customDataDirectory)
                ? customDataDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketMC");
            return Path.Combine(baseDir, "settings.json");
        }

        public SettingsManager(ILogger<SettingsManager>? logger = null)
            : this(ResolveDefaultSettingsFilePath(), logger)
        {
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

        public string GetPersistentDataDirectory()
            => Path.GetDirectoryName(Path.GetFullPath(_settingsFilePath))
                ?? throw new InvalidOperationException("Settings file does not have a parent directory.");

        public AppSettings Load()
        {
            lock (_settingsLock)
            {
                _secretRecoveryBackupCreated = false;
                if (!File.Exists(_settingsFilePath))
                {
                    _settingsWritesBlocked = false;
                    return CreateDefaultSettings();
                }

                AppSettings settings;
                JsonObject? previousDocument = null;
                string originalJson = string.Empty;
                bool canWrite = true;
                int sourceFormatVersion = 0;
                try
                {
                    originalJson = File.ReadAllText(_settingsFilePath);
                    JsonObject sourceDocument = SettingsDocumentCodec.ParseObject(originalJson);
                    sourceFormatVersion = SettingsDocumentCodec.GetFormatVersion(sourceDocument);

                    if (sourceFormatVersion > SettingsDocumentCodec.CurrentFormatVersion)
                    {
                        _settingsWritesBlocked = true;
                        canWrite = false;
                        _logger?.LogWarning(
                            "Settings file format {FormatVersion} is newer than supported format {SupportedVersion}. Writes are disabled to protect user data.",
                            sourceFormatVersion,
                            SettingsDocumentCodec.CurrentFormatVersion);
                        settings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(sourceDocument));
                    }
                    else if (sourceFormatVersion == SettingsDocumentCodec.CurrentFormatVersion)
                    {
                        _settingsWritesBlocked = false;
                        previousDocument = sourceDocument;
                        settings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(sourceDocument));
                    }
                    else
                    {
                        _settingsWritesBlocked = false;
                        JsonObject? snapshot = TryLoadSectionedSnapshot();
                        if (snapshot != null)
                        {
                            previousDocument = SettingsDocumentCodec.MergeLegacyValues(snapshot, sourceDocument);
                            settings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(previousDocument));
                        }
                        else
                        {
                            previousDocument = sourceDocument;
                            settings = SettingsDocumentCodec.DeserializeFlat(sourceDocument);
                        }

                        canWrite = !_settingsWritesBlocked;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                {
                    CreateSettingsBackup("corrupted");
                    _settingsWritesBlocked = true;
                    _logger?.LogError(ex, "Could not safely load {SettingsFilePath}. Its contents were preserved and settings writes are disabled.", _settingsFilePath);
                    return CreateDefaultSettings();
                }

                settings = Normalize(settings);
                if (canWrite)
                {
                    try
                    {
                        ProtectSecrets(settings);
                        JsonObject document = SettingsDocumentCodec.Serialize(settings, previousDocument);
                        string normalizedJson = document.ToJsonString(SettingsJsonOptions);
                        string snapshotPath = SettingsDocumentCodec.GetSnapshotPath(_settingsFilePath);
                        if (normalizedJson != originalJson || !File.Exists(snapshotPath))
                        {
                            if (sourceFormatVersion < SettingsDocumentCodec.CurrentFormatVersion &&
                                originalJson.Length > 0 &&
                                !CreateSettingsBackup("legacy"))
                            {
                                _settingsWritesBlocked = true;
                                _logger?.LogError("Settings migration was not written because the pre-migration backup could not be created.");
                            }
                            else
                            {
                                WriteSettingsDocument(normalizedJson);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to migrate or normalize settings at {SettingsFilePath}; the original content was retained.", _settingsFilePath);
                    }
                }

                UnprotectSecrets(settings);
                return settings;
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            lock (_settingsLock)
            {
                if (_settingsWritesBlocked)
                {
                    throw new InvalidOperationException("Settings writes are disabled because the existing settings file requires recovery or a newer application version.");
                }

                var cloned = CloneSettings(settings);
                JsonObject? previousDocument = null;
                AppSettings? existingSettings = null;
                bool needsPreMigrationBackup = false;

                if (File.Exists(_settingsFilePath))
                {
                    JsonObject existingDocument;
                    try
                    {
                        existingDocument = SettingsDocumentCodec.ParseObject(File.ReadAllText(_settingsFilePath));
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        CreateSettingsBackup("corrupted");
                        _settingsWritesBlocked = true;
                        throw new InvalidOperationException("The settings file could not be parsed and was preserved. Restore or repair it before saving settings.", ex);
                    }

                    int formatVersion = SettingsDocumentCodec.GetFormatVersion(existingDocument);
                    if (formatVersion > SettingsDocumentCodec.CurrentFormatVersion)
                    {
                        _settingsWritesBlocked = true;
                        throw new InvalidOperationException("The settings file was created by a newer application version; refusing to overwrite it.");
                    }

                    needsPreMigrationBackup = formatVersion < SettingsDocumentCodec.CurrentFormatVersion;

                    if (formatVersion == SettingsDocumentCodec.CurrentFormatVersion)
                    {
                        previousDocument = existingDocument;
                        existingSettings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(existingDocument));
                    }
                    else
                    {
                        previousDocument = TryLoadSectionedSnapshot();
                        if (_settingsWritesBlocked)
                        {
                            throw new InvalidOperationException("The settings recovery snapshot is unreadable; refusing to overwrite user data.");
                        }

                        if (previousDocument != null)
                        {
                            previousDocument = SettingsDocumentCodec.MergeLegacyValues(previousDocument, existingDocument);
                            existingSettings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(previousDocument));
                        }
                        else
                        {
                            previousDocument = existingDocument;
                            existingSettings = SettingsDocumentCodec.DeserializeFlat(existingDocument);
                        }
                    }
                }
                else
                {
                    previousDocument = TryLoadSectionedSnapshot();
                    if (_settingsWritesBlocked)
                    {
                        throw new InvalidOperationException("The settings recovery snapshot is unreadable; refusing to overwrite user data.");
                    }

                    if (previousDocument != null)
                    {
                        existingSettings = SettingsDocumentCodec.DeserializeFlat(SettingsDocumentCodec.ToLegacyFlat(previousDocument));
                    }
                }

                // Preserve essential setup state if a caller submits an incomplete settings object.
                if (existingSettings != null && string.IsNullOrWhiteSpace(cloned.AppRootPath) &&
                    !string.IsNullOrWhiteSpace(existingSettings.AppRootPath))
                {
                    cloned.AppRootPath = existingSettings.AppRootPath;
                    cloned.HasCompletedFirstLaunch = existingSettings.HasCompletedFirstLaunch;
                }

                var normalizedSettings = Normalize(cloned);
                ProtectSecrets(normalizedSettings);
                JsonObject outputDocument = SettingsDocumentCodec.Serialize(normalizedSettings, previousDocument);
                if (needsPreMigrationBackup && !CreateSettingsBackup("legacy"))
                {
                    _settingsWritesBlocked = true;
                    throw new InvalidOperationException("Settings were not migrated because the pre-migration backup could not be created.");
                }
                WriteSettingsDocument(outputDocument.ToJsonString(SettingsJsonOptions));
            }

            SettingsSaved?.Invoke(this, CloneSettings(settings));
        }
        public string GetPlayitTomlPath(AppSettings? settings = null)
        {
            var effectiveSettings = Normalize(settings == null ? Load() : CloneSettings(settings));
            return Path.Combine(effectiveSettings.PlayitConfigDirectory!, "playit.toml");
        }

        public System.Collections.Generic.IReadOnlyList<string> GetPlayitPartnerBackendUrls(AppSettings? settings = null)
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable("POCKETMC_PLAYIT_BACKEND_URL");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return new[] { fromEnvironment };
            }
            return AppConfig.AuthProxies;
        }

        private AppSettings CreateDefaultSettings()
        {
            return Normalize(new AppSettings());
        }

        private JsonObject? TryLoadSectionedSnapshot()
        {
            string snapshotPath = SettingsDocumentCodec.GetSnapshotPath(_settingsFilePath);
            if (!File.Exists(snapshotPath))
            {
                return null;
            }

            try
            {
                JsonObject snapshot = SettingsDocumentCodec.ParseObject(File.ReadAllText(snapshotPath));
                if (SettingsDocumentCodec.GetFormatVersion(snapshot) != SettingsDocumentCodec.CurrentFormatVersion)
                {
                    throw new JsonException("The settings recovery snapshot has an unsupported format.");
                }

                return snapshot;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                _settingsWritesBlocked = true;
                _logger?.LogError(ex, "Settings recovery snapshot {SnapshotPath} is unreadable. Settings writes are disabled.", snapshotPath);
                return null;
            }
        }

        private void WriteSettingsDocument(string content)
        {
            string? directory = Path.GetDirectoryName(_settingsFilePath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_settingsFilePath))
            {
                string lastKnownGoodPath = _settingsFilePath + ".last-known-good.json";
                FileUtils.AtomicWriteAllText(lastKnownGoodPath, File.ReadAllText(_settingsFilePath));
            }

            FileUtils.AtomicWriteAllText(SettingsDocumentCodec.GetSnapshotPath(_settingsFilePath), content);
            FileUtils.AtomicWriteAllText(_settingsFilePath, content);
        }

        private bool CreateSettingsBackup(string reason)
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return false;
                }

                string directory = Path.GetDirectoryName(_settingsFilePath) ?? string.Empty;
                string backupPath = Path.Combine(
                    directory,
                    $"settings.json.{reason}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
                File.Copy(_settingsFilePath, backupPath, overwrite: false);
                _logger?.LogInformation("Preserved settings backup at {BackupPath}.", backupPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to preserve settings backup for {SettingsFilePath}.", _settingsFilePath);
                return false;
            }
        }

        private static AppSettings CloneSettings(AppSettings settings)
        {
            string json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        private AppSettings Normalize(AppSettings? settings)
        {
            settings ??= new AppSettings();

            if (!settings.HasMigratedToGreenWallpaperBlurTheme)
            {
                settings.HasMigratedToGreenWallpaperBlurTheme = true;
            }

            if (!settings.HasMigratedToDefaultImageWallpaper)
            {
                if (!settings.HasCompletedFirstLaunch && string.IsNullOrWhiteSpace(settings.CustomBackgroundImagePath))
                {
                    settings.CustomBackgroundImagePath = "pack://application:,,,/Assets/default_wallpaper.png";
                }
                settings.HasMigratedToDefaultImageWallpaper = true;
            }

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
            settings.RemoteControl ??= new RemoteControlSettings();
            settings.DiscordRpc ??= new DiscordRpcSettings();
            if (settings.DiscordRpc.ShowServerAddress && settings.DiscordRpc.ShowVersionAndEngine)
            {
                settings.DiscordRpc.ShowVersionAndEngine = false;
            }
            settings.RemoteControl.Users ??= new System.Collections.Generic.List<RemoteControlUser>();
            foreach (var user in settings.RemoteControl.Users)
            {
                user.AllowedInstanceIds ??= new System.Collections.Generic.List<Guid>();
            }

            if (string.IsNullOrWhiteSpace(settings.RemoteControl.SecurityStamp))
            {
                settings.RemoteControl.SecurityStamp = Guid.NewGuid().ToString();
            }

            if (settings.RemoteControl.Port <= 0 || settings.RemoteControl.Port > 65535)
            {
                settings.RemoteControl.Port = 25580;
            }

            settings.RemoteControl.TunnelProviderId = settings.RemoteControl.AccessMode switch
            {
                RemoteAccessMode.CloudflaredQuickTunnel => "cloudflared-quick",
                RemoteAccessMode.PlayitHttpsTunnel => "playit-https",
                _ => "none"
            };

            settings.SchemaVersion = 2;

            settings.PlayitConfigDirectory ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "playit_gg");

            // Migration: Move old single API key to the dictionary under Gemini
            if (!string.IsNullOrEmpty(settings.AiApiKey))
            {
                if (!settings.AiApiKeys.TryGetValue("Gemini", out string? geminiKey) || string.IsNullOrEmpty(geminiKey))
                {
                    settings.AiApiKeys["Gemini"] = settings.AiApiKey;
                    settings.AiApiKey = null;
                }
                else if (string.Equals(geminiKey, settings.AiApiKey, StringComparison.Ordinal))
                {
                    settings.AiApiKey = null;
                }
                else
                {
                    _logger?.LogWarning("Legacy AI API key conflicts with AiApiKeys.Gemini; both values were preserved.");
                }
            }

            return settings;
        }

        private void ProtectSecrets(AppSettings settings)
        {
            settings.AiApiKey = ProtectSecret(settings.AiApiKey);
            settings.CurseForgeApiKey = ProtectSecret(settings.CurseForgeApiKey);
            settings.DiscordApiKey = ProtectSecret(settings.DiscordApiKey);

            if (settings.PlayitPartnerConnection != null)
            {
                settings.PlayitPartnerConnection.AgentSecretKey = ProtectSecret(settings.PlayitPartnerConnection.AgentSecretKey);
            }

            foreach (var key in new System.Collections.Generic.List<string>(settings.AiApiKeys.Keys))
            {
                string value = settings.AiApiKeys[key];
                if (!string.IsNullOrEmpty(value))
                {
                    settings.AiApiKeys[key] = ProtectSecret(value)!;
                }
            }

            foreach (var key in new System.Collections.Generic.List<string>(settings.CloudTokens.Keys))
            {
                var tokenSet = settings.CloudTokens[key];
                if (tokenSet == null)
                {
                    settings.CloudTokens.Remove(key);
                    continue;
                }

                tokenSet.AccessToken = ProtectSecret(tokenSet.AccessToken);
                tokenSet.RefreshToken = ProtectSecret(tokenSet.RefreshToken);
            }
        }

        private void UnprotectSecrets(AppSettings settings)
        {
            settings.AiApiKey = TryUnprotectSetting(settings.AiApiKey, nameof(settings.AiApiKey));
            settings.CurseForgeApiKey = TryUnprotectSetting(settings.CurseForgeApiKey, nameof(settings.CurseForgeApiKey));
            settings.DiscordApiKey = TryUnprotectSetting(settings.DiscordApiKey, nameof(settings.DiscordApiKey));

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
                PreserveSettingsForSecretRecovery();
                _logger?.LogWarning(ex, "Failed to decrypt protected setting {SettingName}. Clearing only that value.", settingName);
                return null;
            }
        }

        private string? ProtectSecret(string? value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.StartsWith("dpapi:v1:", StringComparison.Ordinal) ||
                value.StartsWith("dpapi:v2:", StringComparison.Ordinal))
            {
                return value;
            }

            return DataProtector.Protect(DataProtector.Unprotect(value));
        }

        private void PreserveSettingsForSecretRecovery()
        {
            if (_secretRecoveryBackupCreated)
            {
                return;
            }

            _secretRecoveryBackupCreated = true;
            if (!CreateSettingsBackup("secret-recovery"))
            {
                _settingsWritesBlocked = true;
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
                PreserveSettingsForSecretRecovery();
                _logger?.LogWarning(ex, "Failed to decrypt protected setting {SettingName}. Removing that cloud token provider.", settingName);
                unprotected = null;
                return false;
            }
        }
    }
}

