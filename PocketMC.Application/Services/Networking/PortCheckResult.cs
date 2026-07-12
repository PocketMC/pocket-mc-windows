using System;
using System.Collections.Generic;
using System.Linq;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Represents the outcome of a port validation request.
/// </summary>
public sealed class PortCheckResult
{
    /// <summary>
    /// Initializes a new port check result.
    /// </summary>
    /// <param name="request">The original port check request.</param>
    /// <param name="isSuccessful">Whether all requested checks completed successfully.</param>
    /// <param name="canBindLocally">Whether the port can be bound locally for the requested transport and IP mode.</param>
    /// <param name="failureCode">The primary failure code, if any.</param>
    /// <param name="failureMessage">A user-facing or diagnostic message for the failure.</param>
    /// <param name="lease">The resulting PocketMC lease, if validation succeeded.</param>
    /// <param name="conflicts">The concrete conflicts discovered during validation.</param>
    /// <param name="recommendations">The recovery recommendations generated for the result.</param>
    /// <param name="checkedAtUtc">The UTC timestamp when the check completed.</param>
    public PortCheckResult(
        PortCheckRequest request,
        bool isSuccessful,
        bool canBindLocally,
        PortFailureCode failureCode = PortFailureCode.None,
        string? failureMessage = null,
        PortLease? lease = null,
        IEnumerable<PortConflictInfo>? conflicts = null,
        IEnumerable<PortRecoveryRecommendation>? recommendations = null,
        DateTimeOffset? checkedAtUtc = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        IsSuccessful = isSuccessful;
        CanBindLocally = canBindLocally;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Lease = lease;
        Conflicts = conflicts?.ToArray() ?? Array.Empty<PortConflictInfo>();
        Recommendations = recommendations?.ToArray() ?? Array.Empty<PortRecoveryRecommendation>();
        CheckedAtUtc = checkedAtUtc ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the original port check request.
    /// </summary>
    public PortCheckRequest Request { get; }

    /// <summary>
    /// Gets a value indicating whether all requested checks completed successfully.
    /// </summary>
    public bool IsSuccessful { get; }

    /// <summary>
    /// Gets a value indicating whether the port can be bound locally.
    /// </summary>
    public bool CanBindLocally { get; }

    /// <summary>
    /// Gets the primary failure code for the result.
    /// </summary>
    public PortFailureCode FailureCode { get; }

    /// <summary>
    /// Gets the user-facing or diagnostic message for the failure.
    /// </summary>
    public string? FailureMessage { get; }

    /// <summary>
    /// Gets the resulting PocketMC lease when validation succeeded.
    /// </summary>
    public PortLease? Lease { get; }

    /// <summary>
    /// Gets the concrete conflicts discovered during validation.
    /// </summary>
    public IReadOnlyList<PortConflictInfo> Conflicts { get; }

    /// <summary>
    /// Gets the recovery recommendations generated for the result.
    /// </summary>
    public IReadOnlyList<PortRecoveryRecommendation> Recommendations { get; }

    /// <summary>
    /// Gets the UTC timestamp when the check completed.
    /// </summary>
    public DateTimeOffset CheckedAtUtc { get; }

    /// <summary>
    /// Gets a value indicating whether any concrete conflicts were recorded.
    /// </summary>
    public bool HasConflicts => Conflicts.Count > 0;
}
