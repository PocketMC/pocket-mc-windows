using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Domain.Models;
using PocketMC.Infrastructure.Configuration;

namespace PocketMC.Infrastructure.Instances.Providers;

/// <summary>Installs verified, native Windows executables from official stable Pumpkin releases.</summary>
public sealed class PumpkinProvider : IServerSoftwareProvider
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;
    private readonly ILogger<PumpkinProvider> _logger;

    public string DisplayName => "Pumpkin";

    public PumpkinProvider(HttpClient httpClient, DownloaderService downloader, ILogger<PumpkinProvider> logger)
    {
        _httpClient = httpClient;
        _downloader = downloader;
        _logger = logger;
    }

    public Task<List<MinecraftVersion>> GetAvailableVersionsAsync() => GetAvailableVersionsAsync(CancellationToken.None);

    public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync(CancellationToken cancellationToken)
    {
        string assetName = GetWindowsAssetName();
        string releasesUrl = GetReleasesUrl();
        var versions = new Dictionary<string, MinecraftVersion>(StringComparer.Ordinal);
        for (int page = 1; ; page++)
        {
            using var document = await GetJsonAsync($"{releasesUrl}?per_page=100&page={page}", cancellationToken);
            var releases = document.RootElement;
            if (releases.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Pumpkin's release API returned an invalid release list.");

            foreach (var release in releases.EnumerateArray())
            {
                string? tag = GetString(release, "tag_name");
                if (!IsStableRelease(release) || string.IsNullOrWhiteSpace(tag) || !TryGetAsset(release, assetName, out var asset))
                    continue;

                string url = ValidateDownloadUrl(GetString(asset, "browser_download_url"), tag, assetName);
                versions.TryAdd(tag, new MinecraftVersion
                {
                    Id = tag,
                    Type = "release",
                    ReleaseTime = release.TryGetProperty("published_at", out var published) && published.TryGetDateTime(out var date)
                        ? date.ToUniversalTime() : DateTime.MinValue,
                    Url = url
                });
            }

            if (releases.GetArrayLength() < 100)
                break;
        }

        return versions.Values.OrderByDescending(version => version.ReleaseTime).ToList();
    }

    public async Task<string> DownloadSoftwareAsync(
        string versionId,
        string destinationPath,
        string? loaderVersion = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("Select an exact Pumpkin release.", nameof(versionId));
        if (!string.IsNullOrEmpty(loaderVersion))
            throw new NotSupportedException("Pumpkin uses a native executable and does not support Java mod loaders.");

        string assetName = GetWindowsAssetName();
        using var document = await GetJsonAsync($"{GetReleasesUrl()}/tags/{Uri.EscapeDataString(versionId)}", cancellationToken);
        var release = document.RootElement;
        if (!IsStableRelease(release) || !string.Equals(GetString(release, "tag_name"), versionId, StringComparison.Ordinal))
            throw new InvalidDataException($"Pumpkin {versionId} is not the selected stable release.");
        if (!TryGetAsset(release, assetName, out var asset))
            throw new InvalidOperationException($"Pumpkin {versionId} has no {assetName} executable for this Windows architecture.");

        string url = ValidateDownloadUrl(GetString(asset, "browser_download_url"), versionId, assetName);
        string hash = await GetSha256Async(release, asset, versionId, assetName, cancellationToken);
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _logger.LogInformation("Installing verified Pumpkin {Version} ({Asset}).", versionId, assetName);
        await _downloader.DownloadFileAsync(url, destinationPath, hash, "SHA256", progress, cancellationToken);
        return string.Empty;
    }

    private async Task<string> GetSha256Async(JsonElement release, JsonElement asset, string tag, string assetName, CancellationToken cancellationToken)
    {
        string? digest = GetString(asset, "digest");
        if (!string.IsNullOrWhiteSpace(digest))
        {
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !IsSha256(digest[7..]))
                throw new InvalidDataException("Pumpkin's executable has an invalid SHA256 digest.");
            return digest[7..];
        }

        if (TryGetAsset(release, "checksums.sha256", out var checksumAsset))
        {
            string checksumUrl = ValidateDownloadUrl(GetString(checksumAsset, "browser_download_url"), tag, "checksums.sha256");
            using var request = CreateRequest(checksumUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string checksums = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (string line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 2 && fields[1].TrimStart('*') == assetName && IsSha256(fields[0]))
                    return fields[0];
            }
        }

        throw new InvalidDataException($"Pumpkin {tag} has no valid SHA256 checksum for {assetName}; installation was refused.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"PocketMC.Desktop/{AppConfig.AppVersion}");
        return request;
    }

    private static string GetWindowsAssetName() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "pumpkin-X64-Windows.exe",
        Architecture.Arm64 => "pumpkin-ARM64-Windows.exe",
        _ => throw new PlatformNotSupportedException("Pumpkin requires x64 or ARM64 Windows.")
    };

    private static string GetReleasesUrl()
    {
        if (!Uri.TryCreate(AppConfig.ProviderPumpkinReleases, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.TrimEnd('/') != "/repos/Pumpkin-MC/Pumpkin/releases" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("The Pumpkin release endpoint must use the official GitHub repository over HTTPS.");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string ValidateDownloadUrl(string? url, string tag, string assetName)
    {
        string expectedPath = $"/Pumpkin-MC/Pumpkin/releases/download/{tag}/{assetName}";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            Uri.UnescapeDataString(uri.AbsolutePath) != expectedPath ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Pumpkin returned an executable URL outside the selected official release.");
        return uri.AbsoluteUri;
    }

    private static bool IsStableRelease(JsonElement release) =>
        release.ValueKind == JsonValueKind.Object &&
        release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.False &&
        release.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.False;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetAsset(JsonElement release, string name, out JsonElement asset)
    {
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in assets.EnumerateArray())
            {
                if (string.Equals(GetString(candidate, "name"), name, StringComparison.Ordinal))
                {
                    asset = candidate;
                    return true;
                }
            }
        }
        asset = default;
        return false;
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
