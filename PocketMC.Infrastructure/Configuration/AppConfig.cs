using PocketMC.Infrastructure.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Infrastructure.Configuration
{
    public static class AppConfig
    {
        private const string ConfigResourceName = "PocketMC.Desktop.pocketmc.yml";

        /// <summary>
        /// GitHub Contents API endpoint for pocketmc.yml. Uses the API instead of raw.githubusercontent.com
        /// because Fastly CDN on raw.githubusercontent.com has a 300-second max-age and ignores query
        /// parameters when computing cache keys, making cache-busting impossible. The API endpoint has
        /// only a 60-second TTL and properly supports conditional ETag requests.
        /// </summary>
        public const string RemoteConfigUrl = "https://api.github.com/repos/PocketMC/pocket-mc-windows/contents/pocketmc.yml?ref=master";

        private static readonly object _configLock = new();

        public static IReadOnlyList<string> AuthProxies { get; private set; } = new List<string>
        {
            "https://pocket-mc-proxy-20d5.onrender.com",
            "https://pocket-mc-proxy-n2qx.onrender.com"
        };
        
        public static IReadOnlyList<string> TelemetryProxies { get; private set; } = new List<string>
        {
            "https://pocket-mc-proxy-3fqm.onrender.com/",
            "https://pocket-mc-proxy.onrender.com/"
        };
        
        public static IReadOnlyList<string> DiscordApiUrls { get; private set; } = new List<string>();

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
            // 1. Load embedded baseline from compiled assembly
            LoadEmbeddedConfig();

            // 2. Load locally cached config if previously fetched
            LoadCachedConfig();
        }

        public static void LoadEmbeddedConfig()
        {
            try
            {
                using var stream = OpenConfigStream();
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();
                    ParseYamlContent(content, preserveLocalVersion: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PocketMC AppConfig failed to load embedded configuration: {ex}");
            }
        }

        public static string GetCacheFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "PocketMC", "cached_config.yml");
        }

        public static void LoadCachedConfig()
        {
            try
            {
                string cachePath = GetCacheFilePath();
                if (File.Exists(cachePath))
                {
                    string content = File.ReadAllText(cachePath);
                    ParseYamlContent(content, preserveLocalVersion: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PocketMC AppConfig failed to load cached configuration: {ex}");
            }
        }

        /// <summary>
        /// HttpClient configured to bypass all caching layers. Uses SocketsHttpHandler with a short
        /// pooled connection lifetime so DNS changes and CDN invalidations are picked up promptly.
        /// </summary>
        private static readonly Lazy<HttpClient> _defaultHttpClient = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            return new HttpClient(handler);
        });

        public static void ParseYamlContent(string content, bool preserveLocalVersion = true)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            lock (_configLock)
            {
                try
                {
                    var authProxies = new List<string>();
                    var telemetryProxies = new List<string>();
                    var discordApiUrls = new List<string>();

                    bool inAuth = false;
                    bool inTelemetry = false;
                    bool inDiscord = false;

                    foreach (var rawLine in content.Split('\n'))
                    {
                        // Strip inline comments
                        var trimmed = rawLine.Split('#')[0].Trim();
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;

                        if (trimmed.StartsWith("auth_proxies:"))
                        {
                            inAuth = true;
                            inTelemetry = false;
                            inDiscord = false;
                            continue;
                        }
                        if (trimmed.StartsWith("telemetry_proxies:"))
                        {
                            inTelemetry = true;
                            inAuth = false;
                            inDiscord = false;
                            continue;
                        }
                        if (trimmed.StartsWith("discord_api_urls:"))
                        {
                            inDiscord = true;
                            inAuth = false;
                            inTelemetry = false;
                            continue;
                        }

                        if (trimmed.StartsWith("-") && (inAuth || inTelemetry || inDiscord))
                        {
                            var match = Regex.Match(trimmed, @"-\s*""?([^""\r\n]+)""?");
                            if (match.Success)
                            {
                                var url = match.Groups[1].Value;
                                if (inAuth) authProxies.Add(url);
                                if (inTelemetry) telemetryProxies.Add(url);
                                if (inDiscord) discordApiUrls.Add(url);
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            if (inAuth || inTelemetry || inDiscord)
                            {
                                inAuth = false;
                                inTelemetry = false;
                                inDiscord = false;
                            }

                            if (!preserveLocalVersion)
                            {
                                var versionMatch = Regex.Match(trimmed, @"version:\s*""?([^""\r\n]+)""?");
                                if (versionMatch.Success) AppVersion = versionMatch.Groups[1].Value;
                            }

                            var discordMatch = Regex.Match(trimmed, @"link_discord:\s*""?([^""\r\n]+)""?");
                            if (discordMatch.Success) LinkDiscord = discordMatch.Groups[1].Value;

                            var instagramMatch = Regex.Match(trimmed, @"link_instagram:\s*""?([^""\r\n]+)""?");
                            if (instagramMatch.Success) LinkInstagram = instagramMatch.Groups[1].Value;

                            var feedbackMatch = Regex.Match(trimmed, @"link_feedback:\s*""?([^""\r\n]+)""?");
                            if (feedbackMatch.Success) LinkFeedback = feedbackMatch.Groups[1].Value;

                            var youtubeMatch = Regex.Match(trimmed, @"link_youtube:\s*""?([^""\r\n]+)""?");
                            if (youtubeMatch.Success) LinkYouTube = youtubeMatch.Groups[1].Value;

                            var redditMatch = Regex.Match(trimmed, @"link_reddit:\s*""?([^""\r\n]+)""?");
                            if (redditMatch.Success) LinkReddit = redditMatch.Groups[1].Value;

                            var githubMatch = Regex.Match(trimmed, @"link_github:\s*""?([^""\r\n]+)""?");
                            if (githubMatch.Success) LinkGitHub = githubMatch.Groups[1].Value;

                            var donationMatch = Regex.Match(trimmed, @"link_donation:\s*""?([^""\r\n]+)""?");
                            if (donationMatch.Success) LinkDonation = donationMatch.Groups[1].Value;
                        }
                    }

                    if (authProxies.Count > 0) AuthProxies = authProxies;
                    if (telemetryProxies.Count > 0) TelemetryProxies = telemetryProxies;
                    if (discordApiUrls.Count > 0) DiscordApiUrls = discordApiUrls;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PocketMC AppConfig parse error: {ex}");
                }
            }
        }

        public static async Task<bool> RefreshRemoteConfigAsync(HttpClient? httpClient = null, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(7));

                HttpClient client = httpClient ?? _defaultHttpClient.Value;

                using var request = new HttpRequestMessage(HttpMethod.Get, RemoteConfigUrl);

                // Force CDN to revalidate with the origin server instead of serving a cached copy.
                // - Cache-Control: no-cache tells the CDN not to serve stale content.
                // - Pragma: no-cache is the HTTP/1.0 equivalent for older proxies.
                // - If-None-Match with an empty ETag forces a full 200 response instead of a 304.
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
                request.Headers.TryAddWithoutValidation("If-None-Match", "\"\"");

                // GitHub API requires a User-Agent header and the raw content Accept header
                // to return the file contents directly instead of the JSON metadata envelope.
                request.Headers.TryAddWithoutValidation("User-Agent", $"PocketMC.Desktop/{AppVersion.Replace(" ", "_")}");
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.v3.raw");

                var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string yaml = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(yaml) && yaml.Contains("link_"))
                    {
                        ParseYamlContent(yaml, preserveLocalVersion: true);

                        // Save to local cache file
                        try
                        {
                            string cachePath = GetCacheFilePath();
                            string? dir = Path.GetDirectoryName(cachePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            File.WriteAllText(cachePath, yaml);
                        }
                        catch (Exception cacheEx)
                        {
                            Debug.WriteLine($"Failed to write remote config cache: {cacheEx}");
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently fallback on any network error, timeout, or offline state
                Debug.WriteLine($"PocketMC AppConfig remote refresh failed (offline fallback active): {ex.Message}");
            }

            return false;
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
