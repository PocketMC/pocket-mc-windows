using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Infrastructure;

/// <summary>
/// Ensures only one instance of the application runs at a time.
/// When a secondary instance is launched, it sends a message via a Named Pipe
/// to the primary instance asking it to bring its window to the foreground,
/// and then the secondary instance exits gracefully without crashing.
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = "Global\\PocketMC_SingleInstance_Mutex_8d4b3c2a";
    private const string PipeName = "PocketMC_SingleInstance_Pipe_8d4b3c2a";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Triggered in the primary instance when a secondary instance requests the app to show itself.
    /// </summary>
    public static event Action<string?>? ShowApplicationRequested;

    /// <summary>
    /// Initializes the single instance check.
    /// </summary>
    /// <returns>True if this is the first instance (startup should continue). False if another instance is already running (this instance should exit).</returns>
    public static bool InitializeAsFirstInstance(string[] args)
    {
        bool isFirstInstance;
        _mutex = new Mutex(true, MutexName, out isFirstInstance);

        if (isFirstInstance)
        {
            // We are the first instance, start listening for other instances
            _cancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => ListenForSecondaryInstances(_cancellationTokenSource.Token));
            return true;
        }
        else
        {
            // Not the first instance. Send a message to the first instance to show itself.
            SendShowRequestToPrimaryInstance(args);
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
    }

    public static void Cleanup()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if not owned
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    private static async Task ListenForSecondaryInstances(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(pipeServer);
                var message = await reader.ReadLineAsync();

                if (message != null)
                {
                    if (message == "SHOW")
                    {
                        ShowApplicationRequested?.Invoke(null);
                    }
                    else if (message.StartsWith("URL:"))
                    {
                        ShowApplicationRequested?.Invoke(message.Substring(4));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Ignore transient errors and keep listening
                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static void SendShowRequestToPrimaryInstance(string[] args)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.None);

            // Give it up to 1 second to connect
            pipeClient.Connect(1000);

            using var writer = new StreamWriter(pipeClient) { AutoFlush = true };

            var uriArg = System.Linq.Enumerable.FirstOrDefault(args, a => a.StartsWith("pocketmc://", StringComparison.OrdinalIgnoreCase));
            if (uriArg != null)
            {
                writer.WriteLine($"URL:{uriArg}");
            }
            else
            {
                writer.WriteLine("SHOW");
            }
        }
        catch (Exception)
        {
            // If we fail to send the message, there's not much we can do.
            // The secondary instance will just exit gracefully.
        }
    }
}
