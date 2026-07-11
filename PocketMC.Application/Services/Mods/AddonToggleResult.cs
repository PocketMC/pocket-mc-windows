using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Mods;

public static class AddonToggleErrorCodes
{
    public const string InvalidPath = "InvalidPath";
    public const string ServerRunning = "ServerRunning";
    public const string NotFound = "NotFound";
    public const string TargetExists = "TargetExists";
    public const string FileLocked = "FileLocked";
    public const string Unsupported = "Unsupported";
}

public sealed class AddonToggleResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public AddonInventoryItem? UpdatedItem { get; init; }

    public static AddonToggleResult Ok(string message)
    {
        return new AddonToggleResult
        {
            Success = true,
            Message = message
        };
    }

    public static AddonToggleResult Fail(string errorCode, string message)
    {
        return new AddonToggleResult
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message
        };
    }
}
