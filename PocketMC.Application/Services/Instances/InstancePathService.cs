using PocketMC.Domain.Models;
using System;
using System.IO;
using PocketMC.Application.Services.Shell;

namespace PocketMC.Application.Services.Instances;

/// <summary>
/// Provides path resolution for instance-related files and directories.
/// Centralizes knowledge of the server storage structure.
/// </summary>
public sealed class InstancePathService
{
    public const string MetadataFileName = ".pocket-mc.json";
    public const string EulaFileName = "eula.txt";

    private readonly ApplicationState _applicationState;

    public InstancePathService(ApplicationState applicationState)
    {
        _applicationState = applicationState;
    }

    public string GetServersRoot() => _applicationState.GetServersDirectory();

    public string GetInstancePath(string slug) => Path.Combine(GetServersRoot(), slug);

    public string GetMetadataPath(string instancePath) => Path.Combine(instancePath, MetadataFileName);

    public string GetEulaPath(string instancePath) => Path.Combine(instancePath, EulaFileName);

    public void EnsureServersRootExists()
    {
        if (!_applicationState.IsConfigured)
        {
            throw new InvalidOperationException("PocketMC is not configured with an app root path.");
        }

        string root = GetServersRoot();
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }
    }
}


