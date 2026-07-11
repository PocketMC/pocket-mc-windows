using System;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Captures an in-memory record of a port recovery recommendation produced after a failure.
/// </summary>
public sealed class PortRecoveryHistoryEntry
{
    /// <summary>
    /// Initializes a new recovery history entry.
    /// </summary>
    public PortRecoveryHistoryEntry(
        DateTimeOffset occurredAtUtc,
        Guid? instanceId,
        string? instanceName,
        int port,
        PortProtocol protocol,
        PortIpMode ipMode,
        string? bindAddress,
        PortFailureCode failureCode,
        string? failureMessage,
        PortRecoveryAction action,
        bool isTransient,
        TimeSpan? retryDelay,
        int attemptNumber,
        int maxAttempts,
        int? suggestedPort,
        PortBindingRole bindingRole = PortBindingRole.PrimaryServer,
        PortEngine engine = PortEngine.Unknown,
        string? displayName = null)
    {
        OccurredAtUtc = occurredAtUtc;
        InstanceId = instanceId;
        InstanceName = instanceName;
        Port = port;
        Protocol = protocol;
        IpMode = ipMode;
        BindAddress = bindAddress;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Action = action;
        IsTransient = isTransient;
        RetryDelay = retryDelay;
        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
        SuggestedPort = suggestedPort;
        BindingRole = bindingRole;
        Engine = engine;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets when the recovery recommendation was produced.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// Gets the related instance id, when known.
    /// </summary>
    public Guid? InstanceId { get; }

    /// <summary>
    /// Gets the related instance name, when known.
    /// </summary>
    public string? InstanceName { get; }

    /// <summary>
    /// Gets the failed local port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the failed transport protocol.
    /// </summary>
    public PortProtocol Protocol { get; }

    /// <summary>
    /// Gets the failed IP mode.
    /// </summary>
    public PortIpMode IpMode { get; }

    /// <summary>
    /// Gets the bind address associated with the failure, if any.
    /// </summary>
    public string? BindAddress { get; }

    /// <summary>
    /// Gets the failure code that triggered recovery.
    /// </summary>
    public PortFailureCode FailureCode { get; }

    /// <summary>
    /// Gets the failure message that triggered recovery.
    /// </summary>
    public string? FailureMessage { get; }

    /// <summary>
    /// Gets the recommended recovery action.
    /// </summary>
    public PortRecoveryAction Action { get; }

    /// <summary>
    /// Gets a value indicating whether the failure was treated as transient.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Gets the retry delay, when a retry was recommended.
    /// </summary>
    public TimeSpan? RetryDelay { get; }

    /// <summary>
    /// Gets the zero-based failed attempt number.
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Gets the maximum number of retry attempts allowed.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Gets the suggested replacement port, when one was found.
    /// </summary>
    public int? SuggestedPort { get; }

    /// <summary>
    /// Gets the server-facing purpose of the failed binding.
    /// </summary>
    public PortBindingRole BindingRole { get; }

    /// <summary>
    /// Gets the server engine responsible for the failed binding.
    /// </summary>
    public PortEngine Engine { get; }

    /// <summary>
    /// Gets the human-readable binding name captured when the failure occurred.
    /// </summary>
    public string? DisplayName { get; }
}
