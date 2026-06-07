namespace PocketMC.Desktop.Features.RemoteControl.Models;

public enum RemoteAccessMode
{
    LanOnly,
    CloudflaredQuickTunnel,
    [Obsolete("PlayIt HTTP tunnels are deprecated for Remote Control due to unreliable external API state. Use CloudflaredQuickTunnel instead.", error: true)]
    PlayitHttpTunnel
}
