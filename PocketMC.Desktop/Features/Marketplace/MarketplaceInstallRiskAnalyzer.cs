namespace PocketMC.Desktop.Features.Marketplace;

public sealed record MarketplaceInstallRisk(bool RequiresConfirmation, IReadOnlyList<string> Warnings);

public static class MarketplaceInstallRiskAnalyzer
{
    private static readonly string[] SuspiciousClientOnlyNames =
    [
        "sodium",
        "iris",
        "optifine",
        "canvas",
        "xaero",
        "journeymap",
        "minimap",
        "replaymod"
    ];

    public static MarketplaceInstallRisk Analyze(
        string providerName,
        string projectType,
        string projectTitle,
        string fileName)
    {
        var warnings = new List<string>();

        bool isCurseForgeMod = providerName.Equals("CurseForge", StringComparison.OrdinalIgnoreCase) &&
                               projectType.Contains("mod", StringComparison.OrdinalIgnoreCase);
        if (isCurseForgeMod)
        {
            warnings.Add("CurseForge does not provide reliable server-side compatibility metadata here. Some mods may be client-only and can crash servers.");
        }

        string searchable = $"{projectTitle} {fileName}";
        if (SuspiciousClientOnlyNames.Any(name => searchable.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("This add-on name looks like an obvious client-only mod. Installing client-only mods on a server can crash startup.");
        }

        return new MarketplaceInstallRisk(warnings.Count > 0, warnings);
    }
}
