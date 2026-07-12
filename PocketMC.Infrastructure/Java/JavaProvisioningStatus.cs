using System;

namespace PocketMC.Infrastructure.Java
{
    public enum JavaProvisioningStage
    {
        Idle,
        Queued,
        ResolvingPackage,
        Downloading,
        Extracting,
        Verifying,
        Ready,
        Failed
    }

    public sealed class JavaProvisioningStatus
    {
        public int Version { get; init; }
        public JavaProvisioningStage Stage { get; init; }
        public string Message { get; init; } = string.Empty;
        public double ProgressPercentage { get; init; }
        public bool IsInstalled { get; init; }
        public int Attempt { get; init; }
        public int MaxAttempts { get; init; }
        public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

        public bool IsBusy =>
            Stage is JavaProvisioningStage.Queued
                or JavaProvisioningStage.ResolvingPackage
                or JavaProvisioningStage.Downloading
                or JavaProvisioningStage.Extracting
                or JavaProvisioningStage.Verifying;

        public bool HasError => Stage == JavaProvisioningStage.Failed;
    }
}
