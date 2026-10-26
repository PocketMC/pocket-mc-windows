using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Instances;

/// <summary>Resolves the configured primary world using the shared engine-aware inventory.</summary>
public static class WorldPathResolver
{
    public static string Resolve(string serverDir, InstanceMetadata metadata, ServerConfigurationService? configService = null)
        => WorldInventory.Resolve(serverDir, metadata)[0].DirectoryPath;

    public static string GetRelativeFolderName(InstanceMetadata metadata, ServerConfigurationService? configService, string serverDir)
        => WorldInventory.Resolve(serverDir, metadata)[0].RelativePath;
}
