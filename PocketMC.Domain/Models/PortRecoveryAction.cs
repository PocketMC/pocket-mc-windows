namespace PocketMC.Domain.Models;

/// <summary>
/// Describes the recovery action PocketMC should take, or offer, after a port failure.
/// </summary>
public enum PortRecoveryAction
{
    /// <summary>
    /// No recovery action is needed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Retry the same port without changing configuration.
    /// </summary>
    RetrySamePort,

    /// <summary>
    /// Wait for a bounded backoff delay, then retry the same port.
    /// </summary>
    WaitWithBackoff,

    /// <summary>
    /// Suggest a different available local port to the user.
    /// </summary>
    SuggestNextFreePort,

    /// <summary>
    /// Change to a different available local port when the caller explicitly allows automatic changes.
    /// </summary>
    AutoSwitchToNextFreePort,

    /// <summary>
    /// Abort the current operation and surface the failure to the user.
    /// </summary>
    Abort
}
