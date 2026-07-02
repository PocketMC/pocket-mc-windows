using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Application.Instances.Services;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Application.Instances.Providers;

public interface IServerSoftwareProvider
{
    string DisplayName { get; }

    Task<List<MinecraftVersion>> GetAvailableVersionsAsync();

    Task DownloadSoftwareAsync(string versionId, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

