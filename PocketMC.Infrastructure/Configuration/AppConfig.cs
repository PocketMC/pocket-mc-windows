using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PocketMC.Infrastructure.Telemetry
{
    public static class AppConfig
    {
        private const string ConfigResourceName = "PocketMC.Desktop.pocketmc.yml";
        // Strict 1-second timeout on all YAML-parsing regex calls to prevent ReDoS.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        public static IReadOnlyList<string> AuthProxies { get; } = new List<string>
        {
            "https://pocket-mc-proxy-20d5.onrender.com",
            "https://pocket-mc-proxy-n2qx.onrender.com"
        };
        
        public static IReadOnlyList<string> TelemetryProxies { get; } = new List<string>
        {
            "https://pocket-mc-proxy-3fqm.onrender.com/",
            "https://pocket-mc-proxy.onrender.com/"
        };

        public static string AppVersion { get; private set; } = "1.0.0";
        public static string LinkDiscord { get; private set; } = "https://discord.gg/mWdMr8Mc2m";
        public static string LinkInstagram { get; private set; } = "https://www.instagram.com/thepocketmc";
        public static string LinkFeedback { get; private set; } = "https://docs.google.com/forms/d/e/1FAIpQLSd6cNMawAbvoELxqIF_FobaC3DptKnjQxViDh9XLcyJdNbTAQ/viewform?usp=dialog";
        public static string LinkYouTube { get; private set; } = "https://www.youtube.com/@OfficialPocketMC";
        public static string LinkReddit { get; private set; } = "https://www.reddit.com/r/PocketMC/";
        public static string LinkGitHub { get; private set; } = "https://github.com/PocketMC/pocket-mc-windows";
        public static string LinkDonation { get; private set; } = "https://buymeacoffee.com/sahaj33";

        static AppConfig()
        {
            try
            {
                using var stream = OpenConfigStream();
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();

                    var authProxies = new List<string>();
                    var telemetryProxies = new List<string>();

                    bool inAuth = false;
                    bool inTelemetry = false;

                    foreach (var line in content.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("auth_proxies:"))
                        {
                            inAuth = true;
                            inTelemetry = false;
                            continue;
                        }
                        if (trimmed.StartsWith("telemetry_proxies:"))
                        {
                            inTelemetry = true;
                            inAuth = false;
                            continue;
                        }

                        if (trimmed.StartsWith("-") && (inAuth || inTelemetry))
                        {
                            var match = Regex.Match(trimmed, @"-\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (match.Success)
                            {
                                var url = match.Groups[1].Value;
                                if (inAuth) authProxies.Add(url);
                                if (inTelemetry) telemetryProxies.Add(url);
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                        {
                            if (inAuth || inTelemetry)
                            {
                                inAuth = false;
                                inTelemetry = false;
                            }

                            var versionMatch = Regex.Match(trimmed, @"version:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (versionMatch.Success) AppVersion = versionMatch.Groups[1].Value;

                            var discordMatch = Regex.Match(trimmed, @"link_discord:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (discordMatch.Success) LinkDiscord = discordMatch.Groups[1].Value;

                            var instagramMatch = Regex.Match(trimmed, @"link_instagram:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (instagramMatch.Success) LinkInstagram = instagramMatch.Groups[1].Value;

                            var feedbackMatch = Regex.Match(trimmed, @"link_feedback:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (feedbackMatch.Success) LinkFeedback = feedbackMatch.Groups[1].Value;

                            var youtubeMatch = Regex.Match(trimmed, @"link_youtube:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (youtubeMatch.Success) LinkYouTube = youtubeMatch.Groups[1].Value;

                            var redditMatch = Regex.Match(trimmed, @"link_reddit:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (redditMatch.Success) LinkReddit = redditMatch.Groups[1].Value;

                            var githubMatch = Regex.Match(trimmed, @"link_github:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (githubMatch.Success) LinkGitHub = githubMatch.Groups[1].Value;

                            var donationMatch = Regex.Match(trimmed, @"link_donation:\s*""?([^""\r\n]+)""?", RegexOptions.None, RegexTimeout);
                            if (donationMatch.Success) LinkDonation = donationMatch.Groups[1].Value;
                        }
                    }

                    if (authProxies.Count > 0) AuthProxies = authProxies;
                    if (telemetryProxies.Count > 0) TelemetryProxies = telemetryProxies;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PocketMC AppConfig failed to load embedded configuration: {ex}");
            }
        }

        private static Stream? OpenConfigStream()
        {
            var candidates = new List<Assembly?>();
            candidates.Add(Assembly.GetEntryAssembly());
            candidates.Add(typeof(AppConfig).Assembly);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                candidates.Add(assembly);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in candidates)
            {
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }

                string? assemblyKey = assembly.FullName;
                if (string.IsNullOrWhiteSpace(assemblyKey) || !seen.Add(assemblyKey))
                {
                    continue;
                }

                try
                {
                    var stream = assembly.GetManifestResourceStream(ConfigResourceName);
                    if (stream != null)
                    {
                        return stream;
                    }
                }
                catch (Exception ex)
                {
                    // Some runtime-generated assemblies cannot expose manifest resources.
                    Debug.WriteLine($"PocketMC AppConfig skipped assembly resource lookup for {assembly.FullName}: {ex}");
                }
            }

            return null;
        }
    }
}
