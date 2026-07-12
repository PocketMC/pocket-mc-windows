namespace PocketMC.RemoteControl.Services;

public sealed class RemoteControlActionResult
{
    private RemoteControlActionResult(bool success, RemoteControlActionFailure failure, string? message)
    {
        Success = success;
        Failure = failure;
        Message = message;
    }

    public bool Success { get; }
    public RemoteControlActionFailure Failure { get; }
    public string? Message { get; }

    public static RemoteControlActionResult Successful(string? message = null) =>
        new(true, RemoteControlActionFailure.None, message);

    public static RemoteControlActionResult Failed(RemoteControlActionFailure failure, string message) =>
        new(false, failure, message);
}
