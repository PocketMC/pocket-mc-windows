using System.Collections.Concurrent;

namespace PocketMC.Infrastructure.Instances.Updates;

public sealed class InstanceUpdateLockService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim updateLock = _locks.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        await updateLock.WaitAsync(cancellationToken);
        return new Releaser(updateLock);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _semaphore.Release();
        }
    }
}
