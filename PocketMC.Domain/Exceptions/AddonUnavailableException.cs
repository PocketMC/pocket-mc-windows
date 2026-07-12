namespace PocketMC.Domain.Models;

public sealed class AddonUnavailableException : Exception
{
    public AddonUnavailableException(
        string addonName,
        string provider,
        string? relativePath,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        AddonName = addonName;
        Provider = provider;
        RelativePath = relativePath;
    }

    public string AddonName { get; }
    public string Provider { get; }
    public string? RelativePath { get; }
}
