namespace PocketMC.RemoteControl.Models;

public sealed class RemoteAddonDto
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double SizeKb { get; set; }
    public string LastModified { get; set; } = string.Empty;
    public string AddonType { get; set; } = string.Empty;
}

public sealed class RemoteUninstallAddonRequest
{
    public string AddonPathOrId { get; set; } = string.Empty;
}
