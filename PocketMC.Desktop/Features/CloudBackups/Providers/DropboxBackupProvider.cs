using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.CloudBackups.OAuth;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Features.CloudBackups.Providers;

public class DropboxBackupProvider : ICloudBackupProvider
{
    public const string ClientId = "fie4wk21xomfr30";
    private const string RedirectUri = "http://127.0.0.1:49383/callback";

    public CloudBackupProviderType ProviderType => CloudBackupProviderType.Dropbox;

    private readonly SettingsManager _settingsManager;
    private readonly ILogger<DropboxBackupProvider> _logger;

    public DropboxBackupProvider(SettingsManager settingsManager, ILogger<DropboxBackupProvider> logger)
    {
        _settingsManager = settingsManager;
        _logger = logger;
    }

    private async Task<DropboxClient?> GetClientAsync(CancellationToken ct)
    {
        var settings = _settingsManager.Load();
        if (!settings.CloudTokens.TryGetValue("Dropbox", out var tokens)) return null;

        if (string.IsNullOrEmpty(tokens.AccessToken) && string.IsNullOrEmpty(tokens.RefreshToken)) return null;

        var config = new DropboxClientConfig("PocketMC-Desktop/1.0");

        if (tokens.ExpiresAtUtc.HasValue && tokens.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            if (string.IsNullOrEmpty(tokens.RefreshToken)) return null;

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", tokens.RefreshToken!),
                    new KeyValuePair<string, string>("client_id", ClientId)
                });
                
                var response = await config.HttpClient.PostAsync("https://api.dropbox.com/oauth2/token", content, ct);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                
                tokens.AccessToken = doc.RootElement.GetProperty("access_token").GetString();
                int expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                tokens.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
                
                settings.CloudTokens["Dropbox"] = tokens;
                _settingsManager.Save(settings);
            }
            catch
            {
                return null;
            }
        }

        return new DropboxClient(tokens.AccessToken, tokens.RefreshToken, ClientId, "", config);
    }

    public async Task<CloudBackupConnectionStatus> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            var client = await GetClientAsync(ct);
            if (client == null) return CloudBackupConnectionStatus.Disconnected;

            var acc = await client.Users.GetCurrentAccountAsync();
            return acc != null ? CloudBackupConnectionStatus.Connected : CloudBackupConnectionStatus.Expired;
        }
        catch (DropboxException ex) when (ex.Message.Contains("expired") || ex.Message.Contains("invalid"))
        {
            return CloudBackupConnectionStatus.Expired;
        }
        catch
        {
            return CloudBackupConnectionStatus.Error;
        }
    }

    public async Task<CloudBackupAccount?> GetAccountAsync(CancellationToken ct)
    {
        var client = await GetClientAsync(ct);
        if (client == null) return null;

        var status = await GetStatusAsync(ct);
        if (status != CloudBackupConnectionStatus.Connected) return null;

        var acc = await client.Users.GetCurrentAccountAsync();
        return new CloudBackupAccount
        {
            Provider = ProviderType,
            DisplayName = acc.Name.DisplayName,
            Email = acc.Email,
            Status = status
        };
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        var (codeVerifier, codeChallenge) = PkceHelper.Generate();
        var state = Guid.NewGuid().ToString("N");
        
        string uri = $"https://www.dropbox.com/oauth2/authorize?client_id={ClientId}&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state={state}&token_access_type=offline&code_challenge={codeChallenge}&code_challenge_method=S256";

        var psi = new System.Diagnostics.ProcessStartInfo { FileName = uri, UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);

        var receiver = new LoopbackOAuthReceiver();
        var (code, error) = await receiver.ReceiveCodeAsync(RedirectUri + "/", ct, state);

        if (!string.IsNullOrEmpty(error)) throw new Exception($"Dropbox Auth Error: {error}");
        if (string.IsNullOrEmpty(code)) throw new Exception("No code returned.");

        var client = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        });
        
        var tokenRes = await client.PostAsync("https://api.dropbox.com/oauth2/token", content, ct);
        tokenRes.EnsureSuccessStatusCode();
        var json = await tokenRes.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        
        string accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        string refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : "";
        int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 0;
        string accountId = doc.RootElement.GetProperty("account_id").GetString()!;
        
        var settings = _settingsManager.Load();
        settings.CloudTokens["Dropbox"] = new CloudOAuthTokenSet
        {
            Provider = ProviderType,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(expiresIn) : null,
            AccountId = accountId
        };
        _settingsManager.Save(settings);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        var client = await GetClientAsync(ct);
        if (client != null)
        {
            try { await client.Auth.TokenRevokeAsync(); } catch { }
        }

        var settings = _settingsManager.Load();
        settings.CloudTokens.Remove("Dropbox");
        _settingsManager.Save(settings);
    }

    public Task ValidateAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<CloudBackupUploadResult> UploadBackupAsync(CloudBackupUploadRequest request)
    {
        return await ResilientUploadPolicy.ExecuteAsync(async (cancellationToken) => 
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null) throw new UnauthorizedAccessException("Dropbox token expired or missing.");

            string sanitizedInstance = CloudPathSanitizer.SanitizeFolderName(request.InstanceName);
            string safeName = CloudPathSanitizer.SanitizeFolderName(request.BackupFileName);
            
            string remotePath = $"/{sanitizedInstance}-{request.InstanceId}/{safeName}";

            var fileInfo = new FileInfo(request.LocalZipPath);
            long totalBytes = fileInfo.Length;
            int chunkSize = 8 * 1024 * 1024; // 8 MB

            using var fileStream = File.OpenRead(request.LocalZipPath);
            
            if (totalBytes <= chunkSize)
            {
                var uploaded = await client.Files.UploadAsync(
                    remotePath,
                    WriteMode.Add.Instance,
                    body: fileStream);
                
                request.Progress?.Report(new CloudBackupProgress { Provider = ProviderType, Stage = "Done", BytesUploaded = totalBytes, TotalBytes = totalBytes, Percent = 100, Message = "Finished" });
                return new CloudBackupUploadResult { Success = true, Provider = ProviderType, ProviderFileId = uploaded.Id, RemotePath = uploaded.PathDisplay, BytesUploaded = totalBytes, Recoverable = false };
            }

            byte[] buffer = new byte[chunkSize];
            int bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize, cancellationToken);
            
            var sessionStart = await client.Files.UploadSessionStartAsync(body: new MemoryStream(buffer, 0, bytesRead));
            string sessionId = sessionStart.SessionId;
            long bytesUploaded = bytesRead;

            request.Progress?.Report(new CloudBackupProgress { Provider = ProviderType, Stage = "Uploading", BytesUploaded = bytesUploaded, TotalBytes = totalBytes, Percent = (double)bytesUploaded / totalBytes * 100, Message = "Uploading chunks..." });

            while (bytesUploaded < totalBytes)
            {
                bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize, cancellationToken);
                if (bytesRead == 0) break;

                var cursor = new UploadSessionCursor(sessionId, (ulong)bytesUploaded);
                
                if (bytesUploaded + bytesRead == totalBytes)
                {
                    var commit = new CommitInfo(remotePath, WriteMode.Add.Instance);
                    var finish = await client.Files.UploadSessionFinishAsync(cursor, commit, body: new MemoryStream(buffer, 0, bytesRead));
                    
                    bytesUploaded += bytesRead;
                    request.Progress?.Report(new CloudBackupProgress { Provider = ProviderType, Stage = "Done", BytesUploaded = bytesUploaded, TotalBytes = totalBytes, Percent = 100, Message = "Finished" });
                    
                    return new CloudBackupUploadResult { Success = true, Provider = ProviderType, ProviderFileId = finish.Id, RemotePath = finish.PathDisplay, BytesUploaded = bytesUploaded, Recoverable = false };
                }
                else
                {
                    await client.Files.UploadSessionAppendV2Async(cursor, body: new MemoryStream(buffer, 0, bytesRead));
                    bytesUploaded += bytesRead;
                    request.Progress?.Report(new CloudBackupProgress { Provider = ProviderType, Stage = "Uploading", BytesUploaded = bytesUploaded, TotalBytes = totalBytes, Percent = (double)bytesUploaded / totalBytes * 100, Message = "Uploading chunks..." });
                }
            }

            throw new Exception("Upload completed but finish call was skipped.");
        }, _logger, request.CancellationToken);
    }

    public async Task<IReadOnlyList<CloudRemoteBackupItem>> ListBackupsAsync(Guid instanceId, string instanceName, CancellationToken ct)
    {
        var client = await GetClientAsync(ct);
        if (client == null) return Array.Empty<CloudRemoteBackupItem>();

        string sanitizedInstance = CloudPathSanitizer.SanitizeFolderName(instanceName);
        string remotePath = $"/{sanitizedInstance}-{instanceId}";

        try
        {
            var list = await client.Files.ListFolderAsync(remotePath);
            var results = new List<CloudRemoteBackupItem>();

            foreach (var item in list.Entries.Where(i => i.IsFile))
            {
                results.Add(new CloudRemoteBackupItem
                {
                    Provider = ProviderType,
                    ProviderFileId = item.AsFile.Id,
                    FileName = item.Name,
                    RemotePath = item.PathDisplay,
                    SizeBytes = (long)item.AsFile.Size,
                    CreatedUtc = item.AsFile.ClientModified
                });
            }
            return results;
        }
        catch (ApiException<ListFolderError> ex) when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
        {
            return Array.Empty<CloudRemoteBackupItem>();
        }
    }

    public async Task DeleteBackupAsync(string providerFileId, CancellationToken ct)
    {
        var client = await GetClientAsync(ct);
        if (client == null) return;
        await client.Files.DeleteV2Async(providerFileId);
    }

    public async Task DownloadBackupAsync(string providerFileId, string localDestinationPath, CancellationToken ct)
    {
        var client = await GetClientAsync(ct);
        if (client == null) throw new UnauthorizedAccessException("Dropbox token is expired or missing.");

        using var response = await client.Files.DownloadAsync(providerFileId);
        using var stream = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write);
        var downloadStream = await response.GetContentAsStreamAsync();
        await downloadStream.CopyToAsync(stream, ct);
    }
}
