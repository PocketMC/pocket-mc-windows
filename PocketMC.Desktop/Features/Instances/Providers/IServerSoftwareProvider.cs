using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Instances.Models;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Instances.Providers;

public interface IServerSoftwareProvider
{
    string DisplayName { get; }

    Task<List<MinecraftVersion>> GetAvailableVersionsAsync();

    Task DownloadSoftwareAsync(string versionId, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
