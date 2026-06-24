using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Features.CloudBackups.Providers;

public class OneDriveBackupProvider : ICloudBackupProvider
{
    public const string ClientId = "b6d4713b-afdf-4e6e-bf14-08aa6633d6c9";
    private readonly string[] Scopes = new[] { "Files.ReadWrite.AppFolder", "offline_access", "User.Read" };

    public CloudBackupProviderType ProviderType => CloudBackupProviderType.OneDrive;

    private readonly SettingsManager _settingsManager;
    private readonly ILogger<OneDriveBackupProvider> _logger;
    private readonly HttpClient _httpClient;
    private IPublicClientApplication? _pca;

    public OneDriveBackupProvider(SettingsManager settingsManager, ILogger<OneDriveBackupProvider> logger, IHttpClientFactory httpClientFactory)
    {
        _settingsManager = settingsManager;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("OneDrive");
    }

    private IPublicClientApplication GetPca()
    {
        if (_pca == null)
        {
            _pca = PublicClientApplicationBuilder.Create(ClientId)
                .WithRedirectUri("http://localhost")
                .Build();

            _pca.UserTokenCache.SetBeforeAccess(args =>
            {
                var settings = _settingsManager.Load();
                if (settings.CloudTokens.TryGetValue("OneDrive", out var tokens) && !string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    try { args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(tokens.RefreshToken)); } catch { }
                }
            });

            _pca.UserTokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged)
                {
                    byte[] cacheBytes = args.TokenCache.SerializeMsalV3();
                    var settings = _settingsManager.Load();
                    if (!settings.CloudTokens.ContainsKey("OneDrive"))
                        settings.CloudTokens["OneDrive"] = new CloudOAuthTokenSet { Provider = CloudBackupProviderType.OneDrive };

                    settings.CloudTokens["OneDrive"].RefreshToken = Convert.ToBase64String(cacheBytes);
                    _settingsManager.Save(settings);
                }
            });
        }
        return _pca;
    }

    private async Task<string?> GetValidAccessTokenAsync(CancellationToken ct)
    {
        var pca = GetPca();
        var accounts = await pca.GetAccountsAsync();
        var firstAccount = accounts.FirstOrDefault();

        if (firstAccount != null)
        {
            try
            {
                var result = await pca.AcquireTokenSilent(Scopes, firstAccount).ExecuteAsync(ct);
                return result.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                return null;
            }
        }
        return null;
    }

    public async Task<CloudBackupConnectionStatus> GetStatusAsync(CancellationToken ct)
    {
        var settings = _settingsManager.Load();
        if (!settings.CloudTokens.ContainsKey("OneDrive")) return CloudBackupConnectionStatus.Disconnected;

        string? token = await GetValidAccessTokenAsync(ct);
        return token != null ? CloudBackupConnectionStatus.Connected : CloudBackupConnectionStatus.Expired;
    }

    public async Task<CloudBackupAccount?> GetAccountAsync(CancellationToken ct)
    {
        var pca = GetPca();
        var accounts = await pca.GetAccountsAsync();
        var acc = accounts.FirstOrDefault();
        if (acc == null) return null;

        var status = await GetStatusAsync(ct);
        return new CloudBackupAccount
        {
            Provider = ProviderType,
            DisplayName = acc.Username,
            Email = acc.Username,
            Status = status
        };
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        var pca = GetPca();
        var result = await pca.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(ct);

        var settings = _settingsManager.Load();
        if (!settings.CloudTokens.ContainsKey("OneDrive"))
            settings.CloudTokens["OneDrive"] = new CloudOAuthTokenSet { Provider = ProviderType };

        settings.CloudTokens["OneDrive"].AccessToken = result.AccessToken;
        settings.CloudTokens["OneDrive"].AccountId = result.Account.HomeAccountId.Identifier;
        settings.CloudTokens["OneDrive"].ExpiresAtUtc = result.ExpiresOn;
        _settingsManager.Save(settings);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        var pca = GetPca();
        var accounts = await pca.GetAccountsAsync();
        foreach (var acc in accounts)
        {
            await pca.RemoveAsync(acc);
        }

        var settings = _settingsManager.Load();
        settings.CloudTokens.Remove("OneDrive");
        _settingsManager.Save(settings);
    }

    public Task ValidateAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<CloudBackupUploadResult> UploadBackupAsync(CloudBackupUploadRequest request)
    {
        return await ResilientUploadPolicy.ExecuteAsync(async (cancellationToken) =>
        {
            string? token = await GetValidAccessTokenAsync(cancellationToken);
            if (token == null) throw new UnauthorizedAccessException("OneDrive token expired or missing.");

            string sanitizedInstance = CloudPathSanitizer.SanitizeFolderName(request.InstanceName);
            string safeName = CloudPathSanitizer.SanitizeFolderName(request.BackupFileName);
            string remotePath = $"/{sanitizedInstance}-{request.InstanceId}/{safeName}";

            var createSessionUrl = $"https://graph.microsoft.com/v1.0/me/drive/special/approot:{remotePath}:/createUploadSession";

            var requestMsg = new HttpRequestMessage(HttpMethod.Post, createSessionUrl);
            requestMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            requestMsg.Content = new StringContent("{\"item\": {\"@microsoft.graph.conflictBehavior\": \"replace\"}}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(requestMsg, cancellationToken);
            response.EnsureSuccessStatusCode();

            var sessionJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(sessionJson);
            var uploadUrl = doc.RootElement.GetProperty("uploadUrl").GetString()!;

            var fileInfo = new FileInfo(request.LocalZipPath);
            long totalBytes = fileInfo.Length;

            int chunkSize = 320 * 1024 * 10;

            using var fileStream = File.OpenRead(request.LocalZipPath);
            byte[] buffer = new byte[chunkSize];
            long bytesUploaded = 0;

            while (bytesUploaded < totalBytes)
            {
                int bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize, cancellationToken);
                if (bytesRead == 0) break;

                long endRange = bytesUploaded + bytesRead - 1;
                var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
                chunkContent.Headers.ContentRange = new ContentRangeHeaderValue(bytesUploaded, endRange, totalBytes);

                var chunkMsg = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
                chunkMsg.Content = chunkContent;

                var chunkResponse = await _httpClient.SendAsync(chunkMsg, cancellationToken);
                if (chunkResponse.StatusCode != System.Net.HttpStatusCode.Accepted && chunkResponse.StatusCode != System.Net.HttpStatusCode.Created && chunkResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    chunkResponse.EnsureSuccessStatusCode();
                }

                bytesUploaded += bytesRead;
                request.Progress?.Report(new CloudBackupProgress
                {
                    Provider = ProviderType,
                    Stage = "Uploading",
                    BytesUploaded = bytesUploaded,
                    TotalBytes = totalBytes,
                    Percent = (double)bytesUploaded / totalBytes * 100,
                    Message = "Uploading chunks..."
                });
            }

            return new CloudBackupUploadResult
            {
                Success = true,
                Provider = ProviderType,
                ProviderFileId = remotePath,
                RemotePath = remotePath,
                BytesUploaded = bytesUploaded,
                Recoverable = false
            };
        }, _logger, request.CancellationToken);
    }

    public async Task<IReadOnlyList<CloudRemoteBackupItem>> ListBackupsAsync(Guid instanceId, string instanceName, CancellationToken ct)
    {
        string sanitizedInstance = CloudPathSanitizer.SanitizeFolderName(instanceName);
        string folderPath = $"/{sanitizedInstance}-{instanceId}";
        string url = $"https://graph.microsoft.com/v1.0/me/drive/special/approot:{folderPath}:/children";

        string? token = await GetValidAccessTokenAsync(ct);
        if (token == null) return Array.Empty<CloudRemoteBackupItem>();

        var requestMsg = new HttpRequestMessage(HttpMethod.Get, url);
        requestMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(requestMsg, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<CloudRemoteBackupItem>();
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<CloudRemoteBackupItem>();
        if (doc.RootElement.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                list.Add(new CloudRemoteBackupItem
                {
                    Provider = ProviderType,
                    ProviderFileId = item.GetProperty("id").GetString()!,
                    FileName = item.GetProperty("name").GetString()!,
                    RemotePath = folderPath + "/" + item.GetProperty("name").GetString(),
                    SizeBytes = item.GetProperty("size").GetInt64(),
                    CreatedUtc = item.GetProperty("createdDateTime").GetDateTimeOffset()
                });
            }
        }
        return list;
    }

    public async Task DeleteBackupAsync(string providerFileId, CancellationToken ct)
    {
        string? token = await GetValidAccessTokenAsync(ct);
        if (token == null) return;

        var url = $"https://graph.microsoft.com/v1.0/me/drive/items/{providerFileId}";
        var requestMsg = new HttpRequestMessage(HttpMethod.Delete, url);
        requestMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(requestMsg, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DownloadBackupAsync(string providerFileId, string localDestinationPath, CancellationToken ct)
    {
        string? token = await GetValidAccessTokenAsync(ct);
        if (token == null) throw new UnauthorizedAccessException("OneDrive token is expired or missing.");

        var url = $"https://graph.microsoft.com/v1.0/me/drive/items/{providerFileId}/content";
        var requestMsg = new HttpRequestMessage(HttpMethod.Get, url);
        requestMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var remoteStream = await response.Content.ReadAsStreamAsync(ct);
        using var localStream = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write);
        await remoteStream.CopyToAsync(localStream, ct);
    }
}
