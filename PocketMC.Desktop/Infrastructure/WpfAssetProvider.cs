using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class WpfAssetProvider : IAssetProvider
    {
        private readonly ILogger<WpfAssetProvider> _logger;

        public WpfAssetProvider(ILogger<WpfAssetProvider> logger)
        {
            _logger = logger;
        }

        public Stream? GetAssetStream(string assetName)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Assets/{assetName}");
                var resourceStream = Application.GetResourceStream(uri);
                return resourceStream?.Stream;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load WPF asset stream for '{AssetName}'.", assetName);
                return null;
            }
        }
    }
}
