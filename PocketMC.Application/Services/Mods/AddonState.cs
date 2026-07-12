using PocketMC.Domain.Models;

namespace PocketMC.Application.Services.Mods;

public enum AddonState
{
    Enabled,
    Disabled
}

public enum AddonDisabledBySource
{
    User,
    CrashDoctor,
    CompatibilityWarning,
    Unknown
}
