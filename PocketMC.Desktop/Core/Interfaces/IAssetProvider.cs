using System.IO;

namespace PocketMC.Desktop.Core.Interfaces
{
    public interface IAssetProvider
    {
        /// <summary>
        /// Gets a stream for the specified asset name (e.g., "logo.png").
        /// The caller is responsible for disposing the stream.
        /// </summary>
        Stream? GetAssetStream(string assetName);
    }
}
