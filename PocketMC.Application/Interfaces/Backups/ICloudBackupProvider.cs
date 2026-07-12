using PocketMC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace PocketMC.Application.Interfaces.Backups; public interface ICloudBackupProvider { CloudBackupProviderType ProviderType { get; } Task<CloudBackupConnectionStatus> GetStatusAsync(CancellationToken ct); Task<CloudBackupAccount?> GetAccountAsync(CancellationToken ct); Task ConnectAsync(CancellationToken ct); Task DisconnectAsync(CancellationToken ct); Task ValidateAsync(CancellationToken ct); Task<CloudBackupUploadResult> UploadBackupAsync(CloudBackupUploadRequest request); Task<IReadOnlyList<CloudRemoteBackupItem>> ListBackupsAsync(Guid instanceId, string instanceName, CancellationToken ct); Task DeleteBackupAsync(string providerFileId, CancellationToken ct); Task DownloadBackupAsync(string providerFileId, string localDestinationPath, CancellationToken ct, IProgress<double>? progress = null); }
