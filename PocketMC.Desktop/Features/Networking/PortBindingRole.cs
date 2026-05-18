namespace PocketMC.Desktop.Features.Networking;

/// <summary>
/// Identifies the server-facing purpose of a local port binding.
/// </summary>
public enum PortBindingRole
{
    /// <summary>
    /// The primary game server port for the instance.
    /// </summary>
    PrimaryServer = 0,

    /// <summary>
    /// The Java Edition listener for a Java server.
    /// </summary>
    JavaServer,

    /// <summary>
    /// The native Bedrock Dedicated Server listener.
    /// </summary>
    BedrockServer,

    /// <summary>
    /// The PocketMine-MP Bedrock-compatible listener.
    /// </summary>
    PocketMineServer,

    /// <summary>
    /// The Geyser Bedrock listener used by Java cross-play instances.
    /// </summary>
    GeyserBedrock,

    /// <summary>
    /// The Simple Voice Chat UDP listener used by the voice chat mod/plugin.
    /// </summary>
    SimpleVoiceChat
}
