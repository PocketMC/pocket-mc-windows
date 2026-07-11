using System;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Describes a recommended next step after a port validation failure.
/// </summary>
public sealed class PortRecoveryRecommendation
{
    /// <summary>
    /// Initializes a new recovery recommendation.
    /// </summary>
    /// <param name="failureCode">The failure this recommendation addresses.</param>
    /// <param name="title">A short human-readable title for the recommendation.</param>
    /// <param name="description">The detailed recovery guidance.</param>
    /// <param name="suggestedPort">An alternative port to use, if one is recommended.</param>
    /// <param name="suggestedBindAddress">An alternative bind address to use, if one is recommended.</param>
    /// <param name="suggestedProtocol">A narrowed protocol recommendation, if one applies.</param>
    /// <param name="suggestedIpMode">A narrowed IP mode recommendation, if one applies.</param>
    /// <param name="canAutoApply">Whether PocketMC can safely apply the recommendation automatically.</param>
    /// <param name="requiresUserAction">Whether the recommendation still requires explicit user action.</param>
    /// <param name="action">The concrete recovery action represented by this recommendation.</param>
    /// <param name="isTransient">Whether the underlying failure is expected to be transient.</param>
    /// <param name="retryDelay">The bounded delay before retrying, if a retry is recommended.</param>
    /// <param name="attemptNumber">The zero-based failed attempt number this recommendation was generated for.</param>
    /// <param name="maxAttempts">The maximum number of retry attempts allowed for this recommendation.</param>
    public PortRecoveryRecommendation(
        PortFailureCode failureCode,
        string title,
        string description,
        int? suggestedPort = null,
        string? suggestedBindAddress = null,
        PortProtocol? suggestedProtocol = null,
        PortIpMode? suggestedIpMode = null,
        bool canAutoApply = false,
        bool requiresUserAction = true,
        PortRecoveryAction action = PortRecoveryAction.Abort,
        bool isTransient = false,
        TimeSpan? retryDelay = null,
        int attemptNumber = 0,
        int maxAttempts = 0)
    {
        FailureCode = failureCode;
        Title = title;
        Description = description;
        SuggestedPort = suggestedPort;
        SuggestedBindAddress = suggestedBindAddress;
        SuggestedProtocol = suggestedProtocol;
        SuggestedIpMode = suggestedIpMode;
        CanAutoApply = canAutoApply;
        RequiresUserAction = requiresUserAction;
        Action = action;
        IsTransient = isTransient;
        RetryDelay = retryDelay;
        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
    }

    /// <summary>
    /// Gets the failure code this recommendation is intended to address.
    /// </summary>
    public PortFailureCode FailureCode { get; }

    /// <summary>
    /// Gets a short human-readable title for the recommendation.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the detailed recovery guidance.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the alternative port to use, if one is recommended.
    /// </summary>
    public int? SuggestedPort { get; }

    /// <summary>
    /// Gets the alternative bind address to use, if one is recommended.
    /// </summary>
    public string? SuggestedBindAddress { get; }

    /// <summary>
    /// Gets the recommended protocol adjustment, if one applies.
    /// </summary>
    public PortProtocol? SuggestedProtocol { get; }

    /// <summary>
    /// Gets the recommended IP mode adjustment, if one applies.
    /// </summary>
    public PortIpMode? SuggestedIpMode { get; }

    /// <summary>
    /// Gets a value indicating whether PocketMC can safely apply the recommendation automatically.
    /// </summary>
    public bool CanAutoApply { get; }

    /// <summary>
    /// Gets a value indicating whether the recommendation still requires explicit user action.
    /// </summary>
    public bool RequiresUserAction { get; }

    /// <summary>
    /// Gets the concrete recovery action represented by this recommendation.
    /// </summary>
    public PortRecoveryAction Action { get; }

    /// <summary>
    /// Gets a value indicating whether the underlying failure is expected to be transient.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Gets the bounded delay before retrying, when a retry is recommended.
    /// </summary>
    public TimeSpan? RetryDelay { get; }

    /// <summary>
    /// Gets the zero-based failed attempt number this recommendation was generated for.
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Gets the maximum number of retry attempts allowed for this recommendation.
    /// </summary>
    public int MaxAttempts { get; }
}
