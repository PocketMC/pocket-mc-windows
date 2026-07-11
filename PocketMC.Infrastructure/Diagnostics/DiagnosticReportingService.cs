using PocketMC.Application.Services.Shell;
using PocketMC.Application.Services.Instances;
using PocketMC.Domain.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using PocketMC.Infrastructure.Instances;


namespace PocketMC.Infrastructure.Diagnostics;

public class DiagnosticReportingService
{
    private readonly ApplicationState _appState;
    private readonly InstanceRegistry _instanceRegistry;
    private readonly PortDiagnosticsSnapshotBuilder _portDiagnosticsSnapshotBuilder;
    private readonly ILogger<DiagnosticReportingService> _logger;

    public DiagnosticReportingService(
        ApplicationState appState,
        InstanceRegistry instanceRegistry,
        PortDiagnosticsSnapshotBuilder portDiagnosticsSnapshotBuilder,
        ILogger<DiagnosticReportingService> logger)
    {
        _appState = appState;
        _instanceRegistry = instanceRegistry;
        _portDiagnosticsSnapshotBuilder = portDiagnosticsSnapshotBuilder;
        _logger = logger;
    }

    public async Task<string> GenerateSupportBundleAsync(string outputDirectory)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string bundlePath = Path.Combine(outputDirectory, $"pocketmc-diagnostics-{timestamp}.zip");

        string tempFolder = Path.Combine(Path.GetTempPath(), $"pocketmc-diag-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempFolder);

        try
        {
            await Task.Run(() => GatherData(tempFolder));
            await Task.Run(() => ZipFile.CreateFromDirectory(tempFolder, bundlePath, CompressionLevel.Fastest, false));
            return bundlePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate support bundle.");
            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(tempFolder, true);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean diagnostics temp folder {TempFolder}.", tempFolder);
            }
        }
    }

    private void GatherData(string tempFolder)
    {
        // 1. Gather System Info
        string sysInfoPath = Path.Combine(tempFolder, "system-info.txt");
        File.WriteAllText(sysInfoPath,
            $"OS: {Environment.OSVersion}\n" +
            $"64Bit: {Environment.Is64BitOperatingSystem}\n" +
            $".NET: {Environment.Version}\n" +
            $"AppRoot: {_appState.Settings.AppRootPath ?? "Not Set"}\n" +
            $"Timestamp: {DateTime.UtcNow:O} UTC\n");

        // 1b. Gather port reliability diagnostics
        string networkDir = Path.Combine(tempFolder, "network");
        Directory.CreateDirectory(networkDir);
        var portSnapshot = _portDiagnosticsSnapshotBuilder.Build();
        File.WriteAllText(
            Path.Combine(networkDir, "port-diagnostics.json"),
            JsonSerializer.Serialize(
                portSnapshot,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                }));

        // 2. Gather App Logs
        string appLogsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketMC", "logs");
        if (Directory.Exists(appLogsDir))
        {
            string outAppLogs = Path.Combine(tempFolder, "app-logs");
            Directory.CreateDirectory(outAppLogs);
            foreach (var file in Directory.GetFiles(appLogsDir, "*.log").OrderByDescending(File.GetLastWriteTime).Take(5))
            {
                File.Copy(file, Path.Combine(outAppLogs, Path.GetFileName(file)));
            }
        }

        // 3. Gather Servers Info
        if (_appState.Settings.AppRootPath != null)
        {
            string outServers = Path.Combine(tempFolder, "servers");
            Directory.CreateDirectory(outServers);

            foreach (var instance in _instanceRegistry.GetAll())
            {
                var instanceDir = Path.Combine(outServers, instance.Id.ToString());
                Directory.CreateDirectory(instanceDir);

                string? originalPath = _instanceRegistry.GetPath(instance.Id);
                if (originalPath == null || !Directory.Exists(originalPath)) continue;

                // Copy .pocket-mc.json
                string metaFile = Path.Combine(originalPath, ".pocket-mc.json");
                if (File.Exists(metaFile)) File.Copy(metaFile, Path.Combine(instanceDir, ".pocket-mc.json"));

                // Copy and redact server.properties
                string propsFile = Path.Combine(originalPath, "server.properties");
                if (File.Exists(propsFile))
                {
                    var props = ServerPropertiesParser.Read(propsFile);
                    if (props.ContainsKey("rcon.password")) props["rcon.password"] = "[REDACTED]";
                    ServerPropertiesParser.Write(Path.Combine(instanceDir, "server.properties"), props);
                }

                // Copy last couple of logs
                string logsDir = Path.Combine(originalPath, "logs");
                if (Directory.Exists(logsDir))
                {
                    string outLogs = Path.Combine(instanceDir, "logs");
                    Directory.CreateDirectory(outLogs);

                    foreach (string sessionLogName in new[]
                    {
                        LogConstants.CurrentSessionLogName,
                        LogConstants.LastSessionLogName,
                        LogConstants.LegacySessionLogName
                    })
                    {
                        string sessionLog = Path.Combine(logsDir, sessionLogName);
                        if (File.Exists(sessionLog)) File.Copy(sessionLog, Path.Combine(outLogs, sessionLogName));
                    }

                    string latestLog = Path.Combine(logsDir, "latest.log");
                    if (File.Exists(latestLog)) File.Copy(latestLog, Path.Combine(outLogs, "latest.log"));
                }

                // Copy crash reports
                string crashDir = Path.Combine(originalPath, "crash-reports");
                if (Directory.Exists(crashDir))
                {
                    string outCrash = Path.Combine(instanceDir, "crash-reports");
                    Directory.CreateDirectory(outCrash);
                    foreach (var file in Directory.GetFiles(crashDir, "*.txt").OrderByDescending(File.GetLastWriteTime).Take(3))
                    {
                        File.Copy(file, Path.Combine(outCrash, Path.GetFileName(file)));
                    }
                }
            }
        }
    }
}
