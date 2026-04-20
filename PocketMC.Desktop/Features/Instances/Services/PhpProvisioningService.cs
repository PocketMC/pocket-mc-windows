using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Shell;

namespace PocketMC.Desktop.Features.Instances.Services;

public class PhpProvisioningService
{
    private readonly HttpClient _httpClient;
    private readonly DownloaderService _downloader;
    private readonly ApplicationState _applicationState;
    private readonly ILogger<PhpProvisioningService> _logger;

    public PhpProvisioningService(
        HttpClient httpClient,
        DownloaderService downloader,
        ApplicationState applicationState,
        ILogger<PhpProvisioningService> logger)
    {
        _httpClient = httpClient;
        _downloader = downloader;
        _applicationState = applicationState;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any(x => x.Product?.Name == "PocketMC.Desktop"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PocketMC.Desktop/1.3.0");
        }
    }

    public virtual bool IsPhpPresent()
    {
        string appRoot = _applicationState.GetRequiredAppRootPath();
        string phpExePath = Path.Combine(appRoot, "runtimes", "php", "bin", "php", "php.exe");
        return File.Exists(phpExePath);
    }

    public virtual async Task EnsurePhpAsync(IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (IsPhpPresent())
        {
            return;
        }

        string appRoot = _applicationState.GetRequiredAppRootPath();
        string runtimesDir = Path.Combine(appRoot, "runtimes");
        string phpDir = Path.Combine(runtimesDir, "php");
        string tempZipPath = Path.Combine(runtimesDir, "php_temp.zip");

        Directory.CreateDirectory(runtimesDir);

        try
        {
            _logger.LogInformation("Resolving official PocketMine PHP binaries...");
            
            // Get the specific tag recommended for PM5
            var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://api.github.com/repos/pmmp/PHP-Binaries/releases/tags/pm5-php-8.2-latest", cancellationToken);
            var assets = response?["assets"] as JsonArray;

            string? downloadUrl = null;
            if (assets != null)
            {
                var windowsAsset = assets.FirstOrDefault(a => 
                    a is JsonObject aObj && 
                    aObj["name"]?.ToString().Contains("Windows-x64-PM5") == true &&
                    aObj["name"]?.ToString().EndsWith(".zip") == true) as JsonObject;
                
                downloadUrl = windowsAsset?["browser_download_url"]?.ToString();
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new Exception("Could not find a valid Windows PHP binary in the PM5 latest tag.");
            }

            _logger.LogInformation("Downloading official PocketMine PHP binary...");
            await _downloader.DownloadFileAsync(downloadUrl, tempZipPath, null, progress, cancellationToken);

            _logger.LogInformation("Extracting PHP binary...");
            if (Directory.Exists(phpDir))
            {
                Directory.Delete(phpDir, true);
            }
            
            await _downloader.ExtractZipAsync(tempZipPath, phpDir, progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision PHP runtime.");
            throw;
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                try
                {
                    File.Delete(tempZipPath);
                }
                catch { }
            }
        }
    }
}
