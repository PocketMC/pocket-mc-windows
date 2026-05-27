using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Instances.Services;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Features.Diagnostics;

public sealed class SupportBundleService
{
    private const long MaxCopiedFileBytes = 2 * 1024 * 1024;
    private readonly SettingsManager _settingsManager;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly SupportBundleRedactor _redactor;
    private readonly ILogger<SupportBundleService> _logger;

    public SupportBundleService(
        SettingsManager settingsManager,
        InstanceRegistry instanceRegistry,
        SupportBundleRedactor redactor,
        ILogger<SupportBundleService> logger)
    {
        _settingsManager = settingsManager;
        _instanceRegistry = instanceRegistry;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<string> CreateAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));

        Directory.CreateDirectory(destinationDirectory);
        string stagingDir = Path.Combine(Path.GetTempPath(), $"PocketMC-support-{Guid.NewGuid():N}");
        string zipPath = Path.Combine(destinationDirectory, $"PocketMC-support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");

        try
        {
            Directory.CreateDirectory(stagingDir);
            await WriteSummaryAsync(stagingDir, cancellationToken);
            await WriteSanitizedSettingsAsync(stagingDir, cancellationToken);
            await WriteInstanceSummaryAsync(stagingDir, cancellationToken);
            await CopyKnownDiagnosticsAsync(stagingDir, cancellationToken);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create support bundle.");
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up support bundle staging directory {StagingDir}.", stagingDir);
            }
        }
    }

    private static Task WriteSummaryAsync(string stagingDir, CancellationToken cancellationToken)
    {
        string summary = $"""
PocketMC Support Bundle
GeneratedUtc: {DateTime.UtcNow:O}
OS: {Environment.OSVersion}
DotNet: {Environment.Version}
Process64Bit: {Environment.Is64BitProcess}
OS64Bit: {Environment.Is64BitOperatingSystem}
""";
        return File.WriteAllTextAsync(Path.Combine(stagingDir, "summary.txt"), summary, cancellationToken);
    }

    private async Task WriteSanitizedSettingsAsync(string stagingDir, CancellationToken cancellationToken)
    {
        var settings = _settingsManager.Load();
        var safe = new
        {
            settings.SchemaVersion,
            HasAppRootPath = !string.IsNullOrWhiteSpace(settings.AppRootPath),
            settings.HasCompletedFirstLaunch,
            settings.StartWithWindows,
            settings.StartMinimizedToTray,
            settings.MinimizeToTrayOnClose,
            settings.WindowBackdrop,
            HasCurseForgeKey = !string.IsNullOrWhiteSpace(settings.CurseForgeApiKey),
            settings.EnableAiSummarization,
            settings.AiProvider,
            HasAiKey = !string.IsNullOrWhiteSpace(settings.GetCurrentAiKey()),
            settings.AlwaysAutoSummarize,
            settings.ConsoleBufferSize,
            settings.EnableDiscordRpc,
            CloudBackupsEnabled = settings.CloudBackups?.Providers?.Any(p => p.IsEnabled) == true
        };

        string json = JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "settings-summary.json"), json, cancellationToken);
    }

    private async Task WriteInstanceSummaryAsync(string stagingDir, CancellationToken cancellationToken)
    {
        var instances = _instanceRegistry.GetAll()
            .Select(instance => new
            {
                instance.SchemaVersion,
                instance.Id,
                instance.Name,
                instance.ServerType,
                instance.MinecraftVersion,
                instance.LoaderVersion,
                instance.CreatedAt,
                instance.LastPlayedAt,
                instance.MinRamMb,
                instance.MaxRamMb,
                instance.HasGeyser,
                instance.GeyserBedrockPort,
                instance.ServerPort,
                instance.BackupIntervalHours,
                instance.MaxBackupsToKeep,
                instance.LastBackupTime,
                instance.EnableAutoRestart,
                HasCustomJavaPath = !string.IsNullOrWhiteSpace(instance.CustomJavaPath),
                HasAdvancedJvmArgs = !string.IsNullOrWhiteSpace(instance.AdvancedJvmArgs),
                instance.SimpleVoiceChatDetected,
                instance.SimpleVoiceChatPort,
                HasSimpleVoiceChatTunnel = !string.IsNullOrWhiteSpace(instance.SimpleVoiceChatTunnelId),
                instance.SimpleVoiceChatStatus
            })
            .ToList();

        string json = JsonSerializer.Serialize(instances, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "instances-summary.json"), json, cancellationToken);
    }

    private async Task CopyKnownDiagnosticsAsync(string stagingDir, CancellationToken cancellationToken)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pocketMcDir = Path.Combine(localAppData, "PocketMC");
        if (!Directory.Exists(pocketMcDir)) return;

        string diagnosticsDir = Path.Combine(stagingDir, "diagnostics");
        Directory.CreateDirectory(diagnosticsDir);

        foreach (string candidateDir in new[] { "logs", "CrashReports" })
        {
            string sourceDir = Path.Combine(pocketMcDir, candidateDir);
            if (!Directory.Exists(sourceDir)) continue;

            string targetDir = Path.Combine(diagnosticsDir, candidateDir);
            Directory.CreateDirectory(targetDir);
            foreach (string file in Directory.EnumerateFiles(sourceDir).OrderByDescending(File.GetLastWriteTimeUtc).Take(20))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CopyRedactedTextFileAsync(file, Path.Combine(targetDir, Path.GetFileName(file)), cancellationToken);
            }
        }
    }

    private async Task CopyRedactedTextFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length > MaxCopiedFileBytes) return;

        string content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        string redacted = _redactor.Redact(content);
        await File.WriteAllTextAsync(destinationPath, redacted, Encoding.UTF8, cancellationToken);
    }
}
