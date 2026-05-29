using System.IO;

namespace PocketMC.Desktop.Features.Mods;

/// <summary>
/// A safe fallback metadata scanner for non-Java addon files
/// (Bedrock .mcpack/.mcaddon, Pocketmine .phar, etc.).
/// 
/// This scanner preserves whatever metadata already exists in the state store
/// by returning a minimal metadata object derived only from the file name.
/// It does NOT attempt to open the file as a ZIP/JAR, preventing the
/// "Unknown" corruption that occurs when the Java scanner fails on
/// non-Java file formats.
/// </summary>
public sealed class PassthroughAddonMetadataScanner : IAddonMetadataScanner
{
    public JavaModMetadata Scan(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string displayName = Path.GetFileNameWithoutExtension(fileName);

        return new JavaModMetadata
        {
            DisplayName = displayName,
            FileName = fileName,
            // Preserve a neutral loader type so downstream code knows
            // this was not scanned by the Java pipeline.
            LoaderType = "Native",
            // No warnings — the file format is expected for this engine.
        };
    }
}
