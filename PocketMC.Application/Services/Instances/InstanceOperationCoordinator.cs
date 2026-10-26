using System.Collections.Concurrent;

namespace PocketMC.Application.Services.Instances;

/// <summary>Coordinates mutations and startup across UI, HTTP and background entry points.</summary>
public sealed class InstanceOperationCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Running = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Admissions = new(1, 1);
    private static readonly SemaphoreSlim MaintenanceSession = new(1, 1);
    private static int _maintenanceRequested;
    private static int _generation;

    public async Task<IDisposable> AcquireAsync(string directory, bool requiresStopped = false, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _maintenanceRequested) != 0) throw new InvalidOperationException("File operations are paused for maintenance.");
        int generation = Volatile.Read(ref _generation);
        string key = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        await Admissions.WaitAsync(cancellationToken);
        SemaphoreSlim gate;
        try
        {
            if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _maintenanceRequested) != 0)
                throw new InvalidOperationException("File operations are paused for maintenance.");
            gate = Gates.GetOrAdd(key, _ => new(1, 1));
        }
        finally { Admissions.Release(); }
        // Never hold the admissions lock while waiting for a busy instance. Its
        // owner may need a different gate (for example Java provisioning) to finish.
        await gate.WaitAsync(cancellationToken);
        if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _maintenanceRequested) != 0 || (requiresStopped && Running.ContainsKey(key)))
        {
            gate.Release();
            throw new InvalidOperationException("Stop the server and wait for other operations before changing its files.");
        }
        return new Release(gate);
    }

    public void SetRunning(string directory, bool running)
    {
        string key = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (running) Running[key] = 1; else Running.TryRemove(key, out _);
    }

    public async Task<IDisposable> BeginMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        await MaintenanceSession.WaitAsync(cancellationToken);
        SemaphoreSlim[] gates;
        try
        {
            await Admissions.WaitAsync(cancellationToken);
            try
            {
                Interlocked.Exchange(ref _maintenanceRequested, 1);
                Interlocked.Increment(ref _generation);
                gates = Gates.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Value).ToArray();
            }
            finally { Admissions.Release(); }
        }
        catch { MaintenanceSession.Release(); throw; }
        var held = new List<SemaphoreSlim>();
        try
        {
            foreach (var gate in gates)
            { await gate.WaitAsync(cancellationToken); held.Add(gate); }
            if (!Running.IsEmpty) throw new InvalidOperationException("All servers must be stopped before maintenance.");
            return new MaintenanceRelease(held);
        }
        catch
        {
            foreach (var gate in held) gate.Release();
            Interlocked.Exchange(ref _maintenanceRequested, 0);
            MaintenanceSession.Release();
            throw;
        }
    }

    public static string FindInstanceRoot(string worldPath)
    {
        for (var current = new DirectoryInfo(worldPath); current != null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, InstancePathService.MetadataFileName))) return current.FullName;
        return Path.GetDirectoryName(Path.GetFullPath(worldPath))!;
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); }
    }
    private sealed class MaintenanceRelease(List<SemaphoreSlim> held) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            foreach (var gate in held) gate.Release();
            Interlocked.Exchange(ref _maintenanceRequested, 0);
            MaintenanceSession.Release();
        }
    }
}
