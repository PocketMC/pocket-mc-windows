using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Infrastructure.Java
{
    public record JavaPackageInfo(string Url, string? Sha256);

    /// <summary>
    /// Client for the Adoptium (Eclipse Temurin) API to resolve Minecraft-compatible Java runtimes.
    /// </summary>
    public sealed class JavaAdoptiumClient
    {
        private const string DownloadClientName = "PocketMC.Downloads";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<JavaAdoptiumClient> _logger;

        public JavaAdoptiumClient(IHttpClientFactory httpClientFactory, ILogger<JavaAdoptiumClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the latest download URL and SHA256 hash for a specific Java version on Windows x64.
        /// Tries JRE first, then falls back to JDK if JRE is unavailable.
        /// </summary>
        public async Task<JavaPackageInfo> ResolveRuntimePackageAsync(int version, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Attempting to resolve JRE package for Java {Version}...", version);
                return await ResolveWithImageTypeAsync(version, "jre", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve JRE package for Java {Version}. Retrying with JDK...", version);
                try
                {
                    return await ResolveWithImageTypeAsync(version, "jdk", cancellationToken);
                }
                catch (Exception jdkEx)
                {
                    throw new InvalidOperationException($"Could not resolve package metadata for Java {version} from Adoptium API (tried both JRE and JDK).", jdkEx);
                }
            }
        }

        private async Task<JavaPackageInfo> ResolveWithImageTypeAsync(int version, string imageType, CancellationToken cancellationToken)
        {
            string os = OperatingSystem.IsLinux() ? "linux" : "windows";
        string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            _ => "x64"
        };
        string apiUrl = $"https://api.adoptium.net/v3/assets/latest/{version}/hotspot?os={os}&architecture={arch}&image_type={imageType}";

            const int maxAttempts = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using HttpClient client = _httpClientFactory.CreateClient(DownloadClientName);
                    string jsonResponse = await client.GetStringAsync(apiUrl, cancellationToken);

                    JsonArray? array = JsonNode.Parse(jsonResponse)?.AsArray();
                    if (array == null || array.Count == 0)
                    {
                        throw new InvalidOperationException($"Adoptium API returned empty response for Java {version} ({imageType}).");
                    }

                    var binary = array[0]?["binary"];
                    var package = binary?["package"];

                    string? link = package?["link"]?.ToString();
                    string? checksum = package?["checksum"]?.ToString();

                    if (string.IsNullOrWhiteSpace(link))
                    {
                        throw new InvalidOperationException($"Could not find a valid download link in Adoptium response for Java {version} ({imageType}).");
                    }

                    return new JavaPackageInfo(link, checksum);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Failed to resolve Java {Version} ({ImageType}) package metadata (attempt {Attempt}/{MaxAttempts}).", version, imageType, attempt, maxAttempts);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
            }

            throw lastException ?? new InvalidOperationException($"Could not resolve package metadata for Java {version} ({imageType}) from Adoptium API.");
        }

        public bool IsRetryable(Exception ex) =>
            ex is HttpRequestException or System.IO.IOException or TaskCanceledException;

        public TimeSpan GetRetryDelay(int attempt) => attempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10)
        };
    }
}
