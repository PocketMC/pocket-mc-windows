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
                if (settings != null && !string.IsNullOrEmpty(settings.CurseForgeApiKey))
                {
                    settings.CurseForgeApiKey = DataProtector.Unprotect(settings.CurseForgeApiKey);
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

            var originalKey = normalizedSettings.CurseForgeApiKey;
            try
            {
                if (!string.IsNullOrEmpty(normalizedSettings.CurseForgeApiKey))
                {
                    normalizedSettings.CurseForgeApiKey = DataProtector.Protect(normalizedSettings.CurseForgeApiKey);
                }
                var content = JsonSerializer.Serialize(normalizedSettings, new JsonSerializerOptions { WriteIndented = true });
                FileUtils.AtomicWriteAllText(_settingsFilePath, content);
            }
            finally
            {
                normalizedSettings.CurseForgeApiKey = originalKey;
            }
        }

        public string GetPlayitTomlPath(AppSettings? settings = null)
        {
            var effectiveSettings = Normalize(settings ?? Load());
            return Path.Combine(effectiveSettings.PlayitConfigDirectory!, "playit.toml");
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

            return settings;
        }
    }
}
