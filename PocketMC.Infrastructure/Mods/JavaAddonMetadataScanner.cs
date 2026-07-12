using PocketMC.Application.Services.Mods;
using PocketMC.Application.Interfaces.Instances;
using PocketMC.Domain.Models;
using System.IO;
using PocketMC.Application.Interfaces;

namespace PocketMC.Infrastructure.Mods;

/// <summary>
/// A metadata scanner that wraps the existing <see cref="JavaModMetadataService.ScanJar"/>
/// for Java-based server engines (Vanilla, Spigot, Fabric, Forge, NeoForge).
/// </summary>
public sealed class JavaAddonMetadataScanner : IAddonMetadataScanner
{
    public JavaModMetadata Scan(string filePath)
    {
        return JavaModMetadataService.ScanJar(filePath);
    }
}
