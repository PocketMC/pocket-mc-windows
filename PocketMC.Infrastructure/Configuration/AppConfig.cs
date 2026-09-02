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

        public static string AppName { get; private set; } = "PocketMC";
        public static string AppTitle { get; private set; } = "PocketMC";
        public static string AppId { get; private set; } = "PocketMC";
        public static string AppVersion { get; private set; } = "1.0.0";
        public static string LinkDiscord { get; private set; } = "https://discord.gg/mWdMr8Mc2m";
        public static string LinkInstagram { get; private set; } = "https://www.instagram.com/thepocketmc";
        public static string LinkFeedback { get; private set; } = "https://docs.google.com/forms/d/e/1FAIpQLSd6cNMawAbvoELxqIF_FobaC3DptKnjQxViDh9XLcyJdNbTAQ/viewform?usp=dialog";
        public static string LinkYouTube { get; private set; } = "https://www.youtube.com/@OfficialPocketMC";
        public static string LinkReddit { get; private set; } = "https://www.reddit.com/r/PocketMC/";
        public static string LinkGitHub { get; private set; } = "https://github.com/PocketMC/pocket-mc-windows";
        public static string LinkReleases { get; private set; } = "https://github.com/PocketMC/pocket-mc-windows/releases";
        public static string LinkWebsite { get; private set; } = "https://ds-labs-portfolio.vercel.app";
        public static string LinkDocs { get; private set; } = "https://github.com/PocketMC/pocket-mc-windows";
        public static string LinkDonation { get; private set; } = "https://buymeacoffee.com/sahaj33";
        public static string LinkOrganization { get; private set; } = "https://ds-labs-portfolio.vercel.app";
        public static string LinkPlayitWebsite { get; private set; } = "https://playit.gg";
        public static string LinkPlayitSetup { get; private set; } = "https://playit.gg/l/setup-third-party";
        public static string LinkPlayitAgents { get; private set; } = "https://playit.gg/account/agents";
        public static string OrganizationName { get; private set; } = "DS Labs";
        public static string OrganizationTagline { get; private set; } = "Selective Digital Studio · Building Software That Works";
        public static string AppDescription { get; private set; } = "Local-first Minecraft server manager for Windows.";

        // Provider & 3rd-Party Service Endpoints
        public static string ProviderMojangManifest { get; private set; } = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        public static string ProviderMojangProfiles { get; private set; } = "https://api.mojang.com/users/profiles/minecraft";
        public static string ProviderPaperMcApi { get; private set; } = "https://fill.papermc.io/v3/projects/paper";
        public static string ProviderPurpurApi { get; private set; } = "https://api.purpurmc.org/v2/purpur";
        public static string ProviderFabricMeta { get; private set; } = "https://meta.fabricmc.net/v2";
        public static string ProviderForgeMeta { get; private set; } = "https://meta.prismlauncher.org/v1/net.minecraftforge/index.json";
        public static string ProviderForgePromotions { get; private set; } = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
        public static string ProviderForgeMaven { get; private set; } = "https://maven.minecraftforge.net/net/minecraftforge/forge";
        public static string ProviderNeoForgeMeta { get; private set; } = "https://meta.prismlauncher.org/v1/net.neoforged/index.json";
        public static string ProviderNeoForgeMaven { get; private set; } = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
        public static string ProviderBedrockKittizz { get; private set; } = "https://raw.githubusercontent.com/kittizz/bedrock-server-downloads/main/bedrock-server-downloads.json";
        public static string ProviderPocketmineReleases { get; private set; } = "https://api.github.com/repos/pmmp/PocketMine-MP/releases";
        public static string ProviderPhpReleases { get; private set; } = "https://api.github.com/repos/pmmp/PHP-Binaries/releases";
        public static string ProviderAdoptiumApi { get; private set; } = "https://api.adoptium.net/v3";
        public static string ProviderModrinthApi { get; private set; } = "https://api.modrinth.com/v2";
        public static string ProviderCurseForgeApi { get; private set; } = "https://api.curseforge.com/v1";
        public static string ProviderPlayitApi { get; private set; } = "https://api.playit.gg";
        public static string ProviderPlayitStatus { get; private set; } = "https://status.playit.gg/api/status?days=1";
        public static string ProviderGeyserApi { get; private set; } = "https://download.geysermc.org/v2/projects";

        // Health Check Endpoints
        public static string HealthCheckPlayit { get; private set; } = "https://playit.gg/";
        public static string HealthCheckAdoptium { get; private set; } = "https://api.adoptium.net/v3/info/release_names?page=0&size=1";
        public static string HealthCheckModrinth { get; private set; } = "https://api.modrinth.com/";

        // Agent Binary Endpoints & Checksums
        public static string BinaryPlayitDownloadUrl { get; private set; } = "https://github.com/playit-cloud/playit-agent/releases/download/v1.0.10/playit-windows-x86_64-signed.exe";
        public static string? BinaryPlayitSha256 { get; private set; } = "2dbdaad119844cbbc062cc9774b8b462afa5f1b4b7832a9fc5ef4676cae887cf";
        public static string BinaryCloudflaredDownloadUrl { get; private set; } = "https://github.com/cloudflare/cloudflared/releases/download/2026.8.1/cloudflared-windows-amd64.exe";
        public static string? BinaryCloudflaredSha256 { get; private set; } = "8f1d6f87b8756dbf37064b16e2c8251b69d816305e4f4373e1b80efb28d13b83";

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

        public static string GetConfigFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "PocketMC", "pocketmc.yml");
        }

        public static string GetCacheFilePath() => GetConfigFilePath();

        public static void LoadCachedConfig() => LoadLocalConfig();

        public static void LoadLocalConfig()
        {
            try
            {
                string configPath = GetConfigFilePath();

                // Gracefully migrate legacy cached_config.yml if found
                if (!File.Exists(configPath))
                {
                    string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketMC", "cached_config.yml");
                    if (File.Exists(legacyPath))
                    {
                        try
                        {
                            File.Move(legacyPath, configPath);
                        }
                        catch
                        {
                            configPath = legacyPath;
                        }
                    }
                }

                if (File.Exists(configPath))
                {
                    string content = File.ReadAllText(configPath);
                    ParseYamlContent(content, preserveLocalVersion: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PocketMC AppConfig failed to load local configuration: {ex}");
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

                                var nameMatch = Regex.Match(trimmed, @"app_name:\s*""?([^""\r\n]+)""?");
                                if (nameMatch.Success) AppName = nameMatch.Groups[1].Value;

                                var titleMatch = Regex.Match(trimmed, @"app_title:\s*""?([^""\r\n]+)""?");
                                if (titleMatch.Success) AppTitle = titleMatch.Groups[1].Value;

                                var idMatch = Regex.Match(trimmed, @"app_id:\s*""?([^""\r\n]+)""?");
                                if (idMatch.Success) AppId = idMatch.Groups[1].Value;
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

                            var releasesMatch = Regex.Match(trimmed, @"link_releases:\s*""?([^""\r\n]+)""?");
                            if (releasesMatch.Success) LinkReleases = releasesMatch.Groups[1].Value;

                            var websiteMatch = Regex.Match(trimmed, @"link_website:\s*""?([^""\r\n]+)""?");
                            if (websiteMatch.Success) LinkWebsite = websiteMatch.Groups[1].Value;

                            var docsMatch = Regex.Match(trimmed, @"link_docs:\s*""?([^""\r\n]+)""?");
                            if (docsMatch.Success) LinkDocs = docsMatch.Groups[1].Value;

                            var orgLinkMatch = Regex.Match(trimmed, @"link_organization:\s*""?([^""\r\n]+)""?");
                            if (orgLinkMatch.Success) LinkOrganization = orgLinkMatch.Groups[1].Value;

                            var orgNameMatch = Regex.Match(trimmed, @"organization_name:\s*""?([^""\r\n]+)""?");
                            if (orgNameMatch.Success) OrganizationName = orgNameMatch.Groups[1].Value;

                            var orgTaglineMatch = Regex.Match(trimmed, @"organization_tagline:\s*""?([^""\r\n]+)""?");
                            if (orgTaglineMatch.Success) OrganizationTagline = orgTaglineMatch.Groups[1].Value;

                            var appDescMatch = Regex.Match(trimmed, @"app_description:\s*""?([^""\r\n]+)""?");
                            if (appDescMatch.Success) AppDescription = appDescMatch.Groups[1].Value;

                            var donationMatch = Regex.Match(trimmed, @"link_donation:\s*""?([^""\r\n]+)""?");
                            if (donationMatch.Success) LinkDonation = donationMatch.Groups[1].Value;

                            var playitWebMatch = Regex.Match(trimmed, @"link_playit_website:\s*""?([^""\r\n]+)""?");
                            if (playitWebMatch.Success) LinkPlayitWebsite = playitWebMatch.Groups[1].Value;

                            var playitSetupMatch = Regex.Match(trimmed, @"link_playit_setup:\s*""?([^""\r\n]+)""?");
                            if (playitSetupMatch.Success) LinkPlayitSetup = playitSetupMatch.Groups[1].Value;

                            var playitAgentsMatch = Regex.Match(trimmed, @"link_playit_agents:\s*""?([^""\r\n]+)""?");
                            if (playitAgentsMatch.Success) LinkPlayitAgents = playitAgentsMatch.Groups[1].Value;

                            var pMojangManifest = Regex.Match(trimmed, @"provider_mojang_manifest:\s*""?([^""\r\n]+)""?");
                            if (pMojangManifest.Success) ProviderMojangManifest = pMojangManifest.Groups[1].Value;

                            var pMojangProfiles = Regex.Match(trimmed, @"provider_mojang_profiles:\s*""?([^""\r\n]+)""?");
                            if (pMojangProfiles.Success) ProviderMojangProfiles = pMojangProfiles.Groups[1].Value;

                            var pPaperMc = Regex.Match(trimmed, @"provider_papermc_api:\s*""?([^""\r\n]+)""?");
                            if (pPaperMc.Success) ProviderPaperMcApi = pPaperMc.Groups[1].Value;

                            var pPurpur = Regex.Match(trimmed, @"provider_purpur_api:\s*""?([^""\r\n]+)""?");
                            if (pPurpur.Success) ProviderPurpurApi = pPurpur.Groups[1].Value;

                            var pFabricMeta = Regex.Match(trimmed, @"provider_fabric_meta:\s*""?([^""\r\n]+)""?");
                            if (pFabricMeta.Success) ProviderFabricMeta = pFabricMeta.Groups[1].Value;

                            var pForgeMeta = Regex.Match(trimmed, @"provider_forge_meta:\s*""?([^""\r\n]+)""?");
                            if (pForgeMeta.Success) ProviderForgeMeta = pForgeMeta.Groups[1].Value;

                            var pForgePromos = Regex.Match(trimmed, @"provider_forge_promotions:\s*""?([^""\r\n]+)""?");
                            if (pForgePromos.Success) ProviderForgePromotions = pForgePromos.Groups[1].Value;

                            var pForgeMaven = Regex.Match(trimmed, @"provider_forge_maven:\s*""?([^""\r\n]+)""?");
                            if (pForgeMaven.Success) ProviderForgeMaven = pForgeMaven.Groups[1].Value;

                            var pNeoForgeMeta = Regex.Match(trimmed, @"provider_neoforge_meta:\s*""?([^""\r\n]+)""?");
                            if (pNeoForgeMeta.Success) ProviderNeoForgeMeta = pNeoForgeMeta.Groups[1].Value;

                            var pNeoForgeMaven = Regex.Match(trimmed, @"provider_neoforge_maven:\s*""?([^""\r\n]+)""?");
                            if (pNeoForgeMaven.Success) ProviderNeoForgeMaven = pNeoForgeMaven.Groups[1].Value;

                            var pBedrock = Regex.Match(trimmed, @"provider_bedrock_kittizz:\s*""?([^""\r\n]+)""?");
                            if (pBedrock.Success) ProviderBedrockKittizz = pBedrock.Groups[1].Value;

                            var pPocketmine = Regex.Match(trimmed, @"provider_pocketmine_releases:\s*""?([^""\r\n]+)""?");
                            if (pPocketmine.Success) ProviderPocketmineReleases = pPocketmine.Groups[1].Value;

                            var pPhp = Regex.Match(trimmed, @"provider_php_releases:\s*""?([^""\r\n]+)""?");
                            if (pPhp.Success) ProviderPhpReleases = pPhp.Groups[1].Value;

                            var pAdoptium = Regex.Match(trimmed, @"provider_adoptium_api:\s*""?([^""\r\n]+)""?");
                            if (pAdoptium.Success) ProviderAdoptiumApi = pAdoptium.Groups[1].Value;

                            var pModrinth = Regex.Match(trimmed, @"provider_modrinth_api:\s*""?([^""\r\n]+)""?");
                            if (pModrinth.Success) ProviderModrinthApi = pModrinth.Groups[1].Value;

                            var pCurseForge = Regex.Match(trimmed, @"provider_curseforge_api:\s*""?([^""\r\n]+)""?");
                            if (pCurseForge.Success) ProviderCurseForgeApi = pCurseForge.Groups[1].Value;

                            var pPlayit = Regex.Match(trimmed, @"provider_playit_api:\s*""?([^""\r\n]+)""?");
                            if (pPlayit.Success) ProviderPlayitApi = pPlayit.Groups[1].Value;

                            var pPlayitStatus = Regex.Match(trimmed, @"provider_playit_status:\s*""?([^""\r\n]+)""?");
                            if (pPlayitStatus.Success) ProviderPlayitStatus = pPlayitStatus.Groups[1].Value;

                            var pGeyser = Regex.Match(trimmed, @"provider_geyser_api:\s*""?([^""\r\n]+)""?");
                            if (pGeyser.Success) ProviderGeyserApi = pGeyser.Groups[1].Value;

                            var hcPlayit = Regex.Match(trimmed, @"health_check_playit:\s*""?([^""\r\n]+)""?");
                            if (hcPlayit.Success) HealthCheckPlayit = hcPlayit.Groups[1].Value;

                            var hcAdoptium = Regex.Match(trimmed, @"health_check_adoptium:\s*""?([^""\r\n]+)""?");
                            if (hcAdoptium.Success) HealthCheckAdoptium = hcAdoptium.Groups[1].Value;

                            var hcModrinth = Regex.Match(trimmed, @"health_check_modrinth:\s*""?([^""\r\n]+)""?");
                            if (hcModrinth.Success) HealthCheckModrinth = hcModrinth.Groups[1].Value;

                            var bPlayitUrl = Regex.Match(trimmed, @"binary_playit_download_url:\s*""?([^""\r\n]+)""?");
                            if (bPlayitUrl.Success) BinaryPlayitDownloadUrl = bPlayitUrl.Groups[1].Value;

                            var bPlayitSha = Regex.Match(trimmed, @"binary_playit_sha256:\s*""?([^""\r\n]+)""?");
                            if (bPlayitSha.Success) BinaryPlayitSha256 = bPlayitSha.Groups[1].Value;

                            var bCloudflaredUrl = Regex.Match(trimmed, @"binary_cloudflared_download_url:\s*""?([^""\r\n]+)""?");
                            if (bCloudflaredUrl.Success) BinaryCloudflaredDownloadUrl = bCloudflaredUrl.Groups[1].Value;

                            var bCloudflaredSha = Regex.Match(trimmed, @"binary_cloudflared_sha256:\s*""?([^""\r\n]+)""?");
                            if (bCloudflaredSha.Success) BinaryCloudflaredSha256 = bCloudflaredSha.Groups[1].Value;
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
                    if (!string.IsNullOrWhiteSpace(yaml) && (yaml.Contains("link_") || yaml.Contains("provider_") || yaml.Contains("app_") || yaml.Contains("version")))
                    {
                        ParseYamlContent(yaml, preserveLocalVersion: true);

                        // Save to local config file
                        try
                        {
                            string configPath = GetConfigFilePath();
                            string? dir = Path.GetDirectoryName(configPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            File.WriteAllText(configPath, yaml);
                        }
                        catch (Exception configEx)
                        {
                            Debug.WriteLine($"Failed to write remote config to local file: {configEx}");
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
