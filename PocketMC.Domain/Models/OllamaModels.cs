using System;
using System.Collections.Generic;

namespace PocketMC.Domain.Models;

public enum OllamaMode
{
    Local,
    Cloud
}

public class OllamaModelInfo
{
    public string Name { get; init; } = string.Empty;
    public string ModelTag { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ParameterSize { get; init; }
    public string? QuantizationLevel { get; init; }
    public string? Family { get; init; }
    public bool IsCloud { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public string FormattedSize
    {
        get
        {
            if (SizeBytes <= 0) return "Unknown";
            const double gb = 1024.0 * 1024.0 * 1024.0;
            const double mb = 1024.0 * 1024.0;
            if (SizeBytes >= gb)
                return $"{SizeBytes / gb:F2} GB";
            return $"{SizeBytes / mb:F1} MB";
        }
    }

    public bool SupportsCompletion =>
        Capabilities.Count == 0 || ContainsCapability("completion");

    private bool ContainsCapability(string capability)
    {
        for (int i = 0; i < Capabilities.Count; i++)
        {
            if (string.Equals(Capabilities[i], capability, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public override string ToString() => Name;
}

public class OllamaPullProgress
{
    public string Status { get; init; } = string.Empty;
    public string? Digest { get; init; }
    public long? TotalBytes { get; init; }
    public long? CompletedBytes { get; init; }
    public bool IsComplete { get; init; }
    public string? ErrorMessage { get; init; }

    public double? Percent
    {
        get
        {
            if (TotalBytes is > 0 && CompletedBytes.HasValue)
                return (double)CompletedBytes.Value / TotalBytes.Value * 100.0;
            return null;
        }
    }

    public static OllamaPullProgress Success() =>
        new() { Status = "success", IsComplete = true };

    public static OllamaPullProgress Error(string message) =>
        new() { Status = "error", IsComplete = true, ErrorMessage = message };
}

public class OllamaDaemonStatus
{
    public bool IsRunning { get; init; }
    public string? Version { get; init; }
    public string? ErrorMessage { get; init; }

    public static OllamaDaemonStatus Online(string version) =>
        new() { IsRunning = true, Version = version };

    public static OllamaDaemonStatus Offline(string error) =>
        new() { IsRunning = false, ErrorMessage = error };
}
