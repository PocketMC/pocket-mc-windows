using Xunit;

namespace PocketMC.Infrastructure.Tests.Storage;

/// <summary>
/// Tests that change <see cref="DownloadTuning"/> mutate process-wide state, so they must never
/// run alongside each other (xUnit parallelises test classes by default).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DownloadTuningCollection
{
    public const string Name = "DownloadTuning";
}
