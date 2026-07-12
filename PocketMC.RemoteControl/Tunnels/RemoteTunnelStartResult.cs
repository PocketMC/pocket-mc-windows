namespace PocketMC.RemoteControl.Tunnels;

public sealed class RemoteTunnelStartResult
{
    public required bool Success { get; init; }
    public string? PublicUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static RemoteTunnelStartResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}
