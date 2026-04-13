using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Features.Instances;
using PocketMC.Desktop.Features.Dashboard;

namespace PocketMC.Desktop.Features.Instances.Providers;

public interface IServerJarProvider
{
    string DisplayName { get; }

    Task<List<MinecraftVersion>> GetAvailableVersionsAsync();

    Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null);
}
