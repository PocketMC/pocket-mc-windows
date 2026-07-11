namespace PocketMC.RemoteControl.Models;

public sealed record RemotePlayerDto
{
    public string Name { get; init; } = string.Empty;
    public bool IsOp { get; init; }
}
