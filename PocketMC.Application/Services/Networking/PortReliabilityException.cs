using System;
using System.Collections.Generic;
using System.Linq;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Represents a startup-blocking failure produced by PocketMC's port reliability pipeline.
/// </summary>
public sealed class PortReliabilityException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new port reliability exception.
    /// </summary>
    /// <param name="results">The failed port check results that blocked startup.</param>
    /// <param name="message">An optional override message for UI or logging.</param>
    public PortReliabilityException(IEnumerable<PortCheckResult> results, string? message = null)
        : base(message ?? BuildMessage(results))
    {
        Results = results?.ToArray() ?? throw new ArgumentNullException(nameof(results));
        if (Results.Count == 0)
        {
            throw new ArgumentException("At least one failed port check result is required.", nameof(results));
        }
    }

    /// <summary>
    /// Gets the failed port check results that blocked startup.
    /// </summary>
    public IReadOnlyList<PortCheckResult> Results { get; }

    /// <summary>
    /// Gets the primary failed result.
    /// </summary>
    public PortCheckResult PrimaryResult => Results[0];

    private static string BuildMessage(IEnumerable<PortCheckResult> results)
    {
        PortCheckResult[] resultArray = results?.ToArray() ?? throw new ArgumentNullException(nameof(results));
        if (resultArray.Length == 0)
        {
            throw new ArgumentException("At least one failed port check result is required.", nameof(results));
        }

        PortCheckResult primary = resultArray[0];
        string message = primary.FailureMessage ?? BuildFallbackMessage(primary);
        PortRecoveryRecommendation? recommendation = primary.Recommendations.FirstOrDefault();

        if (recommendation == null)
        {
            return message;
        }

        return string.IsNullOrWhiteSpace(recommendation.Description)
            ? message
            : $"{message}\n\nSuggested fix: {recommendation.Description}";
    }

    private static string BuildFallbackMessage(PortCheckResult result)
    {
        PortConflictInfo? conflict = result.Conflicts.FirstOrDefault();
        if (conflict != null)
        {
            if (!string.IsNullOrWhiteSpace(conflict.Details))
            {
                return conflict.Details;
            }

            if (conflict.ExistingLease != null)
            {
                string instanceName = conflict.ExistingLease.InstanceName ?? "another PocketMC instance";
                return $"Port {conflict.Port} is already reserved by '{instanceName}'.";
            }

            return $"Port {conflict.Port} is not available.";
        }

        return $"PocketMC could not validate port {result.Request.Port} before startup.";
    }
}
