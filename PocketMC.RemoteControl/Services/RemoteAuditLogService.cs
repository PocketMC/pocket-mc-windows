using System.IO;

namespace PocketMC.RemoteControl.Services;

public sealed class RemoteAuditLogService
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public RemoteAuditLogService()
    {
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PocketMC",
            "logs",
            "remote-actions.log");
    }

    public void Log(string? deviceId, string action, Guid? instanceId = null, string? target = null, bool success = true, string? message = null)
    {
        string line = string.Join('\t', new[]
        {
            DateTimeOffset.UtcNow.ToString("O"),
            Sanitize(deviceId ?? "unknown"),
            Sanitize(action),
            instanceId?.ToString("D") ?? "",
            Sanitize(target ?? ""),
            success ? "success" : "failed",
            Sanitize(message ?? "")
        });

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    private static string Sanitize(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
}
