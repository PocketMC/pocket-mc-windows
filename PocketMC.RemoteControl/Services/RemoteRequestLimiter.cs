using System.Collections.Concurrent;

namespace PocketMC.RemoteControl.Services;

public sealed class RemoteRequestLimiter
{
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new(StringComparer.Ordinal);

    public bool IsBlocked(string scope, string key, int limit, TimeSpan window)
    {
        string counterKey = BuildKey(scope, key);
        if (!_counters.TryGetValue(counterKey, out WindowCounter? counter))
        {
            return false;
        }

        lock (counter)
        {
            if (DateTimeOffset.UtcNow - counter.WindowStartedAtUtc >= window)
            {
                counter.Count = 0;
                counter.WindowStartedAtUtc = DateTimeOffset.UtcNow;
                return false;
            }

            return counter.Count >= limit;
        }
    }

    public bool TryConsume(string scope, string key, int limit, TimeSpan window)
    {
        string counterKey = BuildKey(scope, key);
        WindowCounter counter = _counters.GetOrAdd(counterKey, _ => new WindowCounter(DateTimeOffset.UtcNow));

        lock (counter)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - counter.WindowStartedAtUtc >= window)
            {
                counter.Count = 0;
                counter.WindowStartedAtUtc = now;
            }

            if (counter.Count >= limit)
            {
                return false;
            }

            counter.Count++;
            return true;
        }
    }

    public void RecordFailure(string scope, string key, TimeSpan window)
    {
        string counterKey = BuildKey(scope, key);
        WindowCounter counter = _counters.GetOrAdd(counterKey, _ => new WindowCounter(DateTimeOffset.UtcNow));

        lock (counter)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - counter.WindowStartedAtUtc >= window)
            {
                counter.Count = 0;
                counter.WindowStartedAtUtc = now;
            }

            counter.Count++;
        }
    }

    private static string BuildKey(string scope, string key) => $"{scope}:{key}";

    private sealed class WindowCounter
    {
        public WindowCounter(DateTimeOffset windowStartedAtUtc)
        {
            WindowStartedAtUtc = windowStartedAtUtc;
        }

        public DateTimeOffset WindowStartedAtUtc { get; set; }
        public int Count { get; set; }
    }
}
