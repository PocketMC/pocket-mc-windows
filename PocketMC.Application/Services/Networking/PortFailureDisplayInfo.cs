using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Networking;

/// <summary>
/// Describes user-facing copy for a structured port failure.
/// </summary>
public sealed class PortFailureDisplayInfo
{
    /// <summary>
    /// Initializes display copy for a port failure.
    /// </summary>
    public PortFailureDisplayInfo(string title, string message, string badgeText, string? suggestedAction = null)
    {
        Title = title;
        Message = message;
        BadgeText = badgeText;
        SuggestedAction = suggestedAction;
    }

    /// <summary>
    /// Gets the dialog title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the user-facing dialog message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets compact status text suitable for an instance card badge.
    /// </summary>
    public string BadgeText { get; }

    /// <summary>
    /// Gets the primary suggested action, if one is available.
    /// </summary>
    public string? SuggestedAction { get; }
}
