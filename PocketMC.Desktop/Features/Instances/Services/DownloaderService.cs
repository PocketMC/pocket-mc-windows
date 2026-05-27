using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PocketMC.Desktop.Features.Instances.Models;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Backups;

namespace PocketMC.Desktop.Features.Instances.Services;
    public class DownloaderService
    {
        private const string DownloadClientName = "PocketMC.Downloads";
        private const string PlayitAgentVersion = "0.17.1";
        private const string PlayitDownloadUrl = "https://github.com/playit-cloud/playit-agent/releases/download/v0.17.1/playit-windows-x86_64-signed.exe";
        private const string? PlayitExpectedSha256 = "9b00d6ff7d37d1052e5ae097e1348e11deae8617cd7a8ba39d1777f2006316a3";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DownloaderService> _logger;

        public DownloaderService(IHttpClientFactory httpClientFactory, ILogger<DownloaderService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public Task DownloadFileAsync(
            string url,
            string destinationPath,
            string? expectedHash = null,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return DownloadFileAsync(url, destinationPath, expectedHash, null, progress, cancellationToken);
        }

        public async Task DownloadFileAsync(
            string url, 
            string destinationPath, 
            string? expectedHash,
            string? expectedHashType,
            IProgress<DownloadProgress>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            string partialPath = destinationPath + ".partial";
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            const int maxAttempts = 4;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using HttpClient client = _httpClientFactory.CreateClient(DownloadClientName);
                    long existingBytes = GetExistingPartialLength(partialPath);
                    using HttpRequestMessage request = new(HttpMethod.Get, url);
                    if (existingBytes > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                    }

                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (existingBytes > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        _logger.LogInformation("Server rejected resume for {Url}; restarting download from scratch.", url);
                        TryDeleteFile(partialPath);
                        existingBytes = 0;
                        using HttpRequestMessage restartRequest = new(HttpMethod.Get, url);
                        using HttpResponseMessage restartResponse = await client.SendAsync(restartRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        restartResponse.EnsureSuccessStatusCode();
                        await DownloadToPartialAsync(restartResponse, partialPath, existingBytes, progress, cancellationToken);
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();

                        bool isResuming = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                        if (existingBytes > 0 && !isResuming)
                        {
                            _logger.LogInformation("Resume is not supported for {Url}; restarting download from scratch.", url);
                            TryDeleteFile(partialPath);
                            existingBytes = 0;
                        }

                        await DownloadToPartialAsync(response, partialPath, existingBytes, progress, cancellationToken);
                    }

                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        string hashType = NormalizeHashType(expectedHash, expectedHashType);
                        _logger.LogInformation("Verifying {HashType} hash for {DestinationPath}...", hashType, destinationPath);
                        bool isValid = await VerifyHashAsync(partialPath, expectedHash, hashType, cancellationToken);
                        if (!isValid)
                        {
                            TryDeleteFile(partialPath);
                            throw new CryptographicException($"{hashType} verification failed for {url}. Expected {expectedHash}, but file content mismatched.");
                        }
                        _logger.LogInformation("{HashType} verification passed for {DestinationPath}.", hashType, destinationPath);
                    }

                    await PromoteCompletedDownloadAsync(partialPath, destinationPath, cancellationToken);
                    return;
                }
                catch (Exception ex) when (IsRetryable(ex) && attempt < maxAttempts)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Download attempt {Attempt}/{MaxAttempts} failed for {Url}. Retrying...", attempt, maxAttempts, url);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
            }

            throw new InvalidOperationException($"Failed to download '{url}' after {maxAttempts} attempts.", lastException);
        }

        private static string NormalizeHashType(string expectedHash, string? expectedHashType)
        {
            if (!string.IsNullOrWhiteSpace(expectedHashType))
            {
                return expectedHashType.Replace("-", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
            }

            return expectedHash.Length switch
            {
                40 => "SHA1",
                64 => "SHA256",
                128 => "SHA512",
                _ => throw new NotSupportedException($"Unsupported hash length '{expectedHash.Length}'.")
            };
        }

        private async Task<bool> VerifyHashAsync(string filePath, string expectedHash, string hashType, CancellationToken cancellationToken)
        {
            using HashAlgorithm hasher = hashType switch
            {
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                "SHA512" => SHA512.Create(),
                _ => throw new NotSupportedException($"Unsupported hash type '{hashType}'.")
            };
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            
            byte[] hashBytes = await hasher.ComputeHashAsync(stream, cancellationToken);
            string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            return string.Equals(actualHash, expectedHash.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Downloads playit.exe into &lt;appRoot&gt;/tunnel/playit.exe if not already present.
        /// The executable is staged first and must pass configured verification before promotion.
        /// </summary>
        public async Task EnsurePlayitDownloadedAsync(string appRootPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            string tunnelDir = Path.Combine(appRootPath, "tunnel");
            string playitPath = Path.Combine(tunnelDir, "playit.exe");

            if (File.Exists(playitPath))
            {
                if (await ValidatePlayitExecutableAsync(playitPath, cancellationToken))
                {
                    return;
                }

                _logger.LogWarning("Existing Playit agent at {PlayitPath} failed validation and will be replaced.", playitPath);
                TryDeleteFile(playitPath);
            }

            Directory.CreateDirectory(tunnelDir);

            string stagedPath = Path.Combine(tunnelDir, $"playit-{PlayitAgentVersion}-{Guid.NewGuid():N}.exe");
            try
            {
                await DownloadFileAsync(
                    PlayitDownloadUrl,
                    stagedPath,
                    PlayitExpectedSha256,
                    PlayitExpectedSha256 == null ? null : "SHA256",
                    progress,
                    cancellationToken);

                if (!await ValidatePlayitExecutableAsync(stagedPath, cancellationToken))
                {
                    throw new CryptographicException("Downloaded Playit agent failed executable signature validation.");
                }

                await PromoteCompletedDownloadAsync(stagedPath, playitPath, cancellationToken);
            }
            catch
            {
                TryDeleteFile(stagedPath);
                TryDeleteFile(stagedPath + ".partial");
                throw;
            }
        }

        private async Task<bool> ValidatePlayitExecutableAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(PlayitExpectedSha256))
            {
                return await VerifyHashAsync(filePath, PlayitExpectedSha256, "SHA256", cancellationToken);
            }

            try
            {
                using X509Certificate certificate = X509Certificate.CreateFromSignedFile(filePath);
                string subject = certificate.Subject ?? string.Empty;
                if (string.IsNullOrWhiteSpace(subject))
                {
                    _logger.LogWarning("Playit agent signature exists but has an empty certificate subject: {FilePath}", filePath);
                    return false;
                }

                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                _logger.LogInformation(
                    "Validated signed Playit agent {FilePath}. Publisher={Publisher}, Product={ProductName}, FileVersion={FileVersion}",
                    filePath,
                    subject,
                    versionInfo.ProductName,
                    versionInfo.FileVersion);
                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or FileNotFoundException or ArgumentException)
            {
                _logger.LogError(ex, "Playit agent executable failed signed-file validation: {FilePath}", filePath);
                return false;
            }
        }

        public Task ExtractZipAsync(string zipPath, string extractPath, IProgress<DownloadProgress>? progress = null)
        {
            return SafeZipExtractor.ExtractAsync(
                zipPath,
                extractPath,
                (entriesExtracted, totalEntries) =>
                {
                    progress?.Report(new DownloadProgress
                    {
                        BytesRead = entriesExtracted,
                        TotalBytes = totalEntries
                    });
                });
        }

        private static bool IsRetryable(Exception ex) =>
            ex is HttpRequestException
            or IOException
            or TaskCanceledException;

        private static long GetExistingPartialLength(string partialPath)
        {
            try
            {
                return File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static long GetTotalBytes(HttpContentHeaders headers, long existingBytes, bool isResuming)
        {
            if (isResuming && headers.ContentRange?.Length is long rangedLength && rangedLength > 0)
            {
                return rangedLength;
            }

            if (headers.ContentLength is long contentLength && contentLength > 0)
            {
                return isResuming ? existingBytes + contentLength : contentLength;
            }

            return -1;
        }

        private static async Task DownloadToPartialAsync(
            HttpResponseMessage response,
            string partialPath,
            long existingBytes,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            bool isResuming = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            long totalBytes = GetTotalBytes(response.Content.Headers, existingBytes, isResuming);
            FileMode fileMode = isResuming ? FileMode.Append : FileMode.Create;

            progress?.Report(new DownloadProgress
            {
                BytesRead = existingBytes,
                TotalBytes = totalBytes
            });

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream fileStream = new(partialPath, fileMode, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            byte[] buffer = new byte[81920];
            long totalRead = existingBytes;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                progress?.Report(new DownloadProgress
                {
                    BytesRead = totalRead,
                    TotalBytes = totalBytes
                });
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        private static async Task PromoteCompletedDownloadAsync(string partialPath, string destinationPath, CancellationToken cancellationToken)
        {
            const int maxPromotionAttempts = 5;

            for (int attempt = 1; attempt <= maxPromotionAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }

                    File.Move(partialPath, destinationPath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (attempt < maxPromotionAttempts && ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }

        private static TimeSpan GetRetryDelay(int attempt) => attempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(15)
        };

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }