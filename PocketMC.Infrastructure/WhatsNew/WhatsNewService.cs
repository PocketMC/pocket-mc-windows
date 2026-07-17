using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using PocketMC.Infrastructure.Telemetry;
using PocketMC.Application.Services.Shell;

namespace PocketMC.Infrastructure.WhatsNew
{
    /// <summary>
    /// Orchestrates the "What's New" post-update experience:
    /// version comparison, changelog loading, and mark-as-seen persistence.
    /// </summary>
    public sealed class WhatsNewService
    {
        private const string WhatsNewFileName = "WhatsNew.txt";

        private readonly SettingsManager _settingsManager;
        private readonly ApplicationState _applicationState;
        private readonly ILogger<WhatsNewService> _logger;

        public WhatsNewService(
            SettingsManager settingsManager,
            ApplicationState applicationState,
            ILogger<WhatsNewService> logger)
        {
            _settingsManager = settingsManager;
            _applicationState = applicationState;
            _logger = logger;
        }

        /// <summary>
        /// Returns the current application version as a 3-part string (Major.Minor.Build).
        /// </summary>
        public string GetCurrentVersion()
        {
            return AppConfig.AppVersion;
        }

        /// <summary>
        /// Returns true if the What's New dialog should be shown on this launch.
        /// Compares the current app version with the last seen changelog version.
        /// </summary>
        public bool ShouldShow()
        {
            string currentVersion = GetCurrentVersion();
            string? lastSeen = _applicationState.Settings.LastSeenChangelogVersion;

            bool shouldShow = !string.Equals(currentVersion, lastSeen, StringComparison.OrdinalIgnoreCase);

            if (shouldShow)
            {
                _logger.LogInformation(
                    "What's New check: current={CurrentVersion}, lastSeen={LastSeen} — will show dialog.",
                    currentVersion, lastSeen ?? "(none)");
            }

            return shouldShow;
        }

        /// <summary>
        /// Loads and parses the WhatsNew.txt file from the application directory.
        /// Returns null if the file is missing or cannot be parsed.
        /// </summary>
        public ChangelogEntry? LoadChangelog()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PocketMC.Infrastructure.WhatsNew.txt");

                if (stream == null)
                {
                    _logger.LogWarning("WhatsNew.txt not found in embedded resources. Fallback message will be shown.");
                    return null;
                }

                using var reader = new StreamReader(stream);
                string content = reader.ReadToEnd();
                var entry = ChangelogParser.Parse(content);

                if (entry == null)
                {
                    _logger.LogWarning("Failed to parse WhatsNew.txt — content may be malformed.");
                }

                return entry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading WhatsNew.txt.");
                return null;
            }
        }

        /// <summary>
        /// Persists the current version as the last seen changelog version
        /// so the dialog is not shown again until the next update.
        /// </summary>
        public void MarkAsSeen()
        {
            try
            {
                string currentVersion = GetCurrentVersion();
                _applicationState.Settings.LastSeenChangelogVersion = currentVersion;
                _settingsManager.Save(_applicationState.Settings);

                _logger.LogInformation("What's New marked as seen for version {Version}.", currentVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist LastSeenChangelogVersion.");
            }
        }
    }
}
