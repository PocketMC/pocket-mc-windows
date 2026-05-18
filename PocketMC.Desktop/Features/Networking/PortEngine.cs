namespace PocketMC.Desktop.Features.Networking;

/// <summary>
/// Identifies the server engine responsible for a port binding.
/// </summary>
public enum PortEngine
{
    /// <summary>
    /// The engine could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A Java Edition server engine such as Paper, Fabric, Forge, or Vanilla.
    /// </summary>
    Java,

    /// <summary>
    /// Mojang's Bedrock Dedicated Server engine.
    /// </summary>
    BedrockDedicated,

    /// <summary>
    /// PocketMine-MP.
    /// </summary>
    PocketMine,

    /// <summary>
    /// Geyser's Bedrock protocol listener for Java cross-play.
    /// </summary>
    Geyser,

    /// <summary>
    /// Simple Voice Chat's dedicated UDP voice server.
    /// </summary>
    SimpleVoiceChat
}
