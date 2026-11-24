using PocketMC.Domain.Models;

namespace PocketMC.Infrastructure.Storage;

/// <summary>
/// Runtime download tuning shared by every downloader instance.
/// Values are clamped so a hand-edited settings file cannot exhaust sockets.
/// </summary>
public static class DownloadTuning
{
    public const int MinThreads = 1;
    public const int MaxThreads = 128;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 8;
    public const int DefaultThreads = 8;
    public const int DefaultConcurrency = 3;

    private static int _threads = DefaultThreads;
    private static int _concurrency = DefaultConcurrency;

    public static int Threads => Volatile.Read(ref _threads);

    public static int Concurrency => Volatile.Read(ref _concurrency);

    public static void Configure(int threads, int concurrency)
    {
        Volatile.Write(ref _threads, Math.Clamp(threads, MinThreads, MaxThreads));
        Volatile.Write(ref _concurrency, Math.Clamp(concurrency, MinConcurrency, MaxConcurrency));
    }

    public static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Configure(settings.DownloadThreads, settings.DownloadConcurrency);
    }
}
