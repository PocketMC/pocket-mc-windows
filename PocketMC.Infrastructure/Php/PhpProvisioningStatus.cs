using System;

namespace PocketMC.Infrastructure.Php
{
    public enum PhpProvisioningStage
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

    public sealed class PhpProvisioningStatus
    {
        public string Version { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public PhpProvisioningStage Stage { get; init; }
        public string Message { get; init; } = string.Empty;
        public double ProgressPercentage { get; init; }
        public bool IsInstalled { get; init; }
        public string? ExecutablePath { get; init; }
        public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

        public bool IsBusy =>
            Stage is PhpProvisioningStage.Queued
                or PhpProvisioningStage.ResolvingPackage
                or PhpProvisioningStage.Downloading
                or PhpProvisioningStage.Extracting
                or PhpProvisioningStage.Verifying;

        public bool HasError => Stage == PhpProvisioningStage.Failed;
    }
}
