using System;

using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Captures the details of a detected port conflict.
/// </summary>
public sealed class PortConflictInfo
{
    /// <summary>
    /// Initializes a new port conflict description.
    /// </summary>
    /// <param name="failureCode">The specific conflict classification.</param>
    /// <param name="port">The conflicting local port.</param>
    /// <param name="protocol">The conflicting protocol.</param>
    /// <param name="ipMode">The conflicting IP stack mode.</param>
    /// <param name="bindAddress">The bind address involved in the conflict, if known.</param>
    /// <param name="existingLease">The competing PocketMC lease, if the conflict is internal.</param>
    /// <param name="processId">The external process identifier, if known.</param>
    /// <param name="processName">The external process name, if known.</param>
    /// <param name="details">Additional conflict context suitable for logs or UI.</param>
    public PortConflictInfo(
        PortFailureCode failureCode,
        int port,
        PortProtocol protocol = PortProtocol.Tcp,
        PortIpMode ipMode = PortIpMode.IPv4,
        string? bindAddress = null,
        PortLease? existingLease = null,
        int? processId = null,
        string? processName = null,
        string? details = null)
    {
        FailureCode = failureCode;
        Port = port;
        Protocol = protocol;
        IpMode = ipMode;
        BindAddress = bindAddress;
        ExistingLease = existingLease;
        ProcessId = processId;
        ProcessName = processName;
        Details = details;
    }

    /// <summary>
    /// Gets the specific failure code assigned to the conflict.
    /// </summary>
    public PortFailureCode FailureCode { get; }

    /// <summary>
    /// Gets the conflicting local port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the conflicting protocol.
    /// </summary>
    public PortProtocol Protocol { get; }

    /// <summary>
    /// Gets the conflicting IP stack mode.
    /// </summary>
    public PortIpMode IpMode { get; }

    /// <summary>
    /// Gets the bind address involved in the conflict, if known.
    /// </summary>
    public string? BindAddress { get; }

    /// <summary>
    /// Gets the competing PocketMC lease when the conflict is internal.
    /// </summary>
    public PortLease? ExistingLease { get; }

    /// <summary>
    /// Gets the operating system process identifier when the conflict is external.
    /// </summary>
    public int? ProcessId { get; }

    /// <summary>
    /// Gets the operating system process name when the conflict is external.
    /// </summary>
    public string? ProcessName { get; }

    /// <summary>
    /// Gets additional conflict context suitable for diagnostics or UI.
    /// </summary>
    public string? Details { get; }

    /// <summary>
    /// Gets a value indicating whether the conflict points at another PocketMC lease.
    /// </summary>
    public bool IsPocketMcConflict => ExistingLease != null;

    /// <summary>
    /// Gets a value indicating whether the conflict points at an external process.
    /// </summary>
    public bool IsExternalProcessConflict => ProcessId.HasValue || !string.IsNullOrWhiteSpace(ProcessName);
}
