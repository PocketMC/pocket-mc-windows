using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Domain.Models;
using PocketMC.Application.Services.Instances;

namespace PocketMC.Application.Interfaces.Instances;

public interface IServerSoftwareProvider
{
    string DisplayName { get; }

    Task<List<MinecraftVersion>> GetAvailableVersionsAsync();

    Task<string> DownloadSoftwareAsync(string versionId, string destinationPath, string? loaderVersion = null, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<List<ModLoaderVersion>> GetBuildsAsync(string versionId) => Task.FromResult(new List<ModLoaderVersion>());
}

