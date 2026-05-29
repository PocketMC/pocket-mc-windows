using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Features.Mods;

/// <summary>
/// Factory that selects the correct <see cref="IAddonMetadataScanner"/>
/// based on the server's engine family.
/// 
/// Java engines (Vanilla, Spigot, Fabric, Forge, NeoForge) get the full
/// JAR scanner.  Non-Java engines (Bedrock, Pocketmine) get a safe
/// passthrough scanner that preserves existing metadata instead of
/// corrupting it by forcing a JAR read on incompatible file formats.
/// </summary>
public static class AddonMetadataScannerFactory
{
    private static readonly IAddonMetadataScanner JavaScanner = new JavaAddonMetadataScanner();
    private static readonly IAddonMetadataScanner PassthroughScanner = new PassthroughAddonMetadataScanner();

    /// <summary>
    /// Returns the appropriate metadata scanner for the given engine compatibility.
    /// </summary>
    public static IAddonMetadataScanner GetScanner(EngineCompatibility compatibility)
    {
        if (compatibility.IsJavaEngine)
        {
            return JavaScanner;
        }

        return PassthroughScanner;
    }
}
