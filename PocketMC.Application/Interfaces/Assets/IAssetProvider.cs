using System.IO;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Interfaces
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
