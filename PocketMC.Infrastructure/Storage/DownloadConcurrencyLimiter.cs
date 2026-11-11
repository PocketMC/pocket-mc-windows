namespace PocketMC.Infrastructure.Storage;

/// <summary>
/// Limits concurrent file downloads to <see cref="DownloadTuning.Concurrency"/>.
/// Polling keeps the ceiling adjustable at runtime and makes cancellation leak-free.
/// </summary>
internal sealed class DownloadConcurrencyLimiter
{
    private const int PollMilliseconds = 25;

    private readonly object _sync = new();
    private int _active;

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (_active < DownloadTuning.Concurrency)
                {
                    _active++;
                    return new Lease(this);
                }
            }

            await Task.Delay(PollMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            _active = Math.Max(0, _active - 1);
        }
    }

    private sealed class Lease : IDisposable
    {
        private DownloadConcurrencyLimiter? _owner;

        public Lease(DownloadConcurrencyLimiter owner) => _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }
}
