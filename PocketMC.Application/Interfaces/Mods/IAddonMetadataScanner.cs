using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Mods;

/// <summary>
/// Abstraction for scanning addon files to extract metadata.
/// Each server engine family (Java, Bedrock, Pocketmine) can provide
/// its own implementation to avoid routing non-Java files through
/// the JAR scanner.
/// </summary>
public interface IAddonMetadataScanner
{
    /// <summary>
    /// Scans the addon file at <paramref name="filePath"/> and returns
    /// whatever metadata can be extracted. Implementations must never throw;
    /// they should return a safe default when the file cannot be read.
    /// </summary>
    JavaModMetadata Scan(string filePath);
}
