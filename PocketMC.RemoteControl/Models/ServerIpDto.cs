namespace PocketMC.RemoteControl.Models;

public sealed record ServerIpDto
{
    public string Label { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
}
