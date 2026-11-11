using System.Collections.Concurrent;
using System.Net;
using PocketMC.Infrastructure.Configuration;

namespace PocketMC.Infrastructure.Storage;

/// <summary>Runtime mirror configuration sourced from user settings plus the signed app config.</summary>
public sealed record DownloadMirrorOptions(
    bool Enabled,
    string? CustomMirror,
    IReadOnlyList<string> BuiltInTunnels)
{
    public static DownloadMirrorOptions Disabled { get; } = new(false, null, Array.Empty<string>());

    public static DownloadMirrorOptions FromSettings(
        bool enabled, string? customMirror, IReadOnlyList<string>? builtIn = null)
    {
        return new DownloadMirrorOptions(
            enabled,
            customMirror,
            builtIn ?? AppConfig.DownloadMirrorTunnels);
    }
}

/// <summary>A rewritten URL plus the tunnel template that produced it (null for the direct URL).</summary>
public sealed record DownloadMirrorCandidate(Uri Uri, string? Tunnel);

/// <summary>
/// Builds ordered candidates (custom tunnel, known-good public tunnels, direct URL) for download
/// requests and remembers tunnels that recently failed so dead mirrors are skipped.
/// </summary>
public sealed class DownloadMirrorService
{
    public const string UrlPlaceholder = "{url}";

    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> GitHubHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "codeload.github.com",
        "raw.githubusercontent.com",
        "gist.githubusercontent.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com"
    };

    private const string BmclApiBase = "https://bmclapi2.bangbang93.com";

    // BMCLAPI keeps the same path layout for Mojang metadata and object storage.
    private static readonly Dictionary<string, string> MojangMirrorHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["launchermeta.mojang.com"] = BmclApiBase,
        ["piston-meta.mojang.com"] = BmclApiBase,
        ["piston-data.mojang.com"] = BmclApiBase
    };

    // Maven hosts mapped onto "{mavenMirror}/<path>"; the prefix is stripped from the request path.
    private static readonly Dictionary<string, string> MavenHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["libraries.minecraft.net"] = "",
        ["maven.neoforged.net"] = "releases",
        ["maven.minecraftforge.net"] = "",
        ["files.minecraftforge.net"] = "maven",
        ["repo1.maven.org"] = "maven2",
        ["repo.maven.apache.org"] = "maven2"
    };

    private readonly Func<DownloadMirrorOptions> _optionsAccessor;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedUntil = new(StringComparer.OrdinalIgnoreCase);

    public DownloadMirrorService(Func<DownloadMirrorOptions> optionsAccessor)
    {
        _optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
    }

    public DownloadMirrorOptions Current => _optionsAccessor() ?? DownloadMirrorOptions.Disabled;

    /// <summary>True when the user has not disabled accelerated downloads.</summary>
    public bool MirrorsEnabled => Current.Enabled;

    /// <summary>Base URL of the maven mirror, without a trailing slash; null when unavailable.</summary>
    public string? MavenMirrorBase
    {
        get
        {
            if (!Current.Enabled) return null;
            string? baseUrl = AppConfig.DownloadMavenMirrors.FirstOrDefault();
            return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
        }
    }

    /// <summary>Returns the direct URL only when mirrors are disabled or no mirror applies.</summary>
    public IReadOnlyList<DownloadMirrorCandidate> GetCandidates(Uri original)
    {
        ArgumentNullException.ThrowIfNull(original);

        var direct = new DownloadMirrorCandidate(original, null);
        DownloadMirrorOptions options = Current;
        if (!options.Enabled || !IsMirrorable(original))
        {
            return new[] { direct };
        }

        var mirrors = new List<DownloadMirrorCandidate>();

        if (TryRewrite(options.CustomMirror, original, out Uri? customUri))
        {
            mirrors.Add(new DownloadMirrorCandidate(customUri, "custom"));
        }

        if (TryMapJsDelivr(original, out Uri? jsDelivrUri))
        {
            mirrors.Add(new DownloadMirrorCandidate(jsDelivrUri, "jsdelivr"));
        }

        if (GitHubHosts.Contains(original.Host))
        {
            foreach (string tunnel in options.BuiltInTunnels)
            {
                if (TryRewrite(tunnel, original, out Uri? uri))
                {
                    mirrors.Add(new DownloadMirrorCandidate(uri, tunnel));
                }
            }
        }

        if (TryMapMojang(original, out Uri? mojangUri))
        {
            mirrors.Add(new DownloadMirrorCandidate(mojangUri, "bmclapi"));
        }

        if (TryMapMaven(original, out Uri? mavenUri))
        {
            mirrors.Add(new DownloadMirrorCandidate(mavenUri, "bmclapi-maven"));
        }

        if (mirrors.Count == 0)
        {
            return new[] { direct };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var ordered = mirrors
            .GroupBy(candidate => candidate.Uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => IsCoolingDown(candidate.Tunnel, now) ? 1 : 0)
            .ToList();

        ordered.Add(direct);
        return ordered;
    }

    public void ReportFailure(string? tunnel)
    {
        if (string.IsNullOrWhiteSpace(tunnel))
        {
            return;
        }

        _failedUntil[tunnel] = DateTimeOffset.UtcNow.Add(FailureCooldown);
    }

    public void ReportSuccess(string? tunnel)
    {
        if (!string.IsNullOrWhiteSpace(tunnel))
        {
            _failedUntil.TryRemove(tunnel, out _);
        }
    }

    private bool IsCoolingDown(string? tunnel, DateTimeOffset now)
    {
        return tunnel != null && _failedUntil.TryGetValue(tunnel, out DateTimeOffset until) && until > now;
    }

    /// <summary>Validates a custom template: "{url}" placeholder or a trailing-slash prefix, http(s) only.</summary>
    public static bool IsValidTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        string trimmed = template.Trim();
        string sample = trimmed.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase)
            ? trimmed.Replace(UrlPlaceholder, "https://example.com/file.jar", StringComparison.OrdinalIgnoreCase)
            : trimmed.EndsWith('/') ? trimmed + "https://example.com/file.jar" : string.Empty;

        return sample.Length > 0 &&
               Uri.TryCreate(sample, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>Applies a "{url}" template, or a trailing-slash prefix template.</summary>
    public static bool TryRewrite(string? template, Uri original, out Uri result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        string trimmed = template.Trim();
        string candidate = trimmed.Contains(UrlPlaceholder, StringComparison.OrdinalIgnoreCase)
            ? trimmed.Replace(UrlPlaceholder, original.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            : trimmed.EndsWith('/') ? trimmed + original.AbsoluteUri : string.Empty;

        if (candidate.Length == 0 ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        result = uri;
        return true;
    }

    /// <summary>Maps raw repository files onto jsDelivr, a CDN mirror of GitHub content.</summary>
    private static bool TryMapJsDelivr(Uri original, out Uri result)
    {
        result = null!;
        if (!string.Equals(original.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = original.AbsolutePath.TrimStart('/').Split('/');
        if (parts.Length < 4 || parts.Any(part => part.Length == 0))
        {
            return false;
        }

        string owner = parts[0];
        string repo = parts[1];
        string reference = parts[2];
        string rest = string.Join('/', parts.Skip(3));
        result = new Uri($"https://cdn.jsdelivr.net/gh/{owner}/{repo}@{reference}/{rest}");
        return true;
    }

    /// <summary>Maps Mojang metadata and object storage onto BMCLAPI's mirror tree.</summary>
    private static bool TryMapMojang(Uri original, out Uri result)
    {
        result = null!;
        if (string.Equals(original.Host, "resources.download.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            result = new Uri(BmclApiBase + "/assets" + original.AbsolutePath);
            return true;
        }

        if (!MojangMirrorHosts.TryGetValue(original.Host, out string? mirrorBase))
        {
            return false;
        }

        result = new Uri(mirrorBase + original.PathAndQuery);

        // Version metadata lives at a version-named path on BMCLAPI: /version/<version>/json
        string path = original.AbsolutePath;
        if (path.StartsWith("/v1/packages/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string fileName = path[(path.LastIndexOf('/') + 1)..];
            string version = fileName[..^".json".Length];
            if (version.Length > 0 && version.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                result = new Uri($"{BmclApiBase}/version/{version}/json");
            }
        }

        return true;
    }

    /// <summary>Maps maven repository downloads onto the configured maven mirror.</summary>
    private static bool TryMapMaven(Uri original, out Uri result)
    {
        result = null!;
        if (!MavenHosts.TryGetValue(original.Host, out string? prefix)) return false;

        string? mirrorBase = AppConfig.DownloadMavenMirrors.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(mirrorBase)) return false;

        string path = original.AbsolutePath;
        if (prefix.Length > 0)
        {
            string required = "/" + prefix;
            if (!path.StartsWith(required + "/", StringComparison.OrdinalIgnoreCase)) return false;
            path = path[required.Length..];
        }

        if (!Uri.TryCreate(mirrorBase.TrimEnd('/') + path, UriKind.Absolute, out Uri? mapped)) return false;

        result = mapped;
        return true;
    }

    private static bool IsMirrorable(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (uri.IsLoopback || IPAddress.TryParse(uri.Host, out _))
        {
            return false;
        }

        return !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }
}
