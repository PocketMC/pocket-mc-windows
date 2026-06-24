using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Features.Shell;
using PocketMC.Desktop.Infrastructure;

namespace PocketMC.Desktop.Features.RemoteControl.Tunnels;

public sealed class CloudflaredQuickTunnelProvider : IRemoteTunnelProvider, IDisposable
{
    private static readonly Regex TryCloudflareUrlRegex = new(
        @"https:\/\/[a-zA-Z0-9-]+\.trycloudflare\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private readonly ApplicationState _applicationState;
    private readonly ICloudflaredInstaller _installer;
    private readonly JobObject _jobObject;
    private readonly ILogger<CloudflaredQuickTunnelProvider> _logger;
    private readonly object _lock = new();
    private Process? _process;
    private string? _publicUrl;
    private string? _errorMessage;
    private DateTimeOffset? _startedAtUtc;
    private bool _disposed;

    private readonly SemaphoreSlim _startLock = new(1, 1);

    public CloudflaredQuickTunnelProvider(
        ApplicationState applicationState,
        ICloudflaredInstaller installer,
        JobObject jobObject,
        ILogger<CloudflaredQuickTunnelProvider> logger)
    {
        _applicationState = applicationState;
        _installer = installer;
        _jobObject = jobObject;
        _logger = logger;
    }

    public string Id => "cloudflared-quick";
    public string DisplayName => "Cloudflare Quick Tunnel";

    public static bool TryParsePublicUrl(string? line, out string? publicUrl)
    {
        publicUrl = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        Match match = TryCloudflareUrlRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        publicUrl = match.Value;
        return true;
    }

    public async Task<RemoteTunnelStartResult> StartAsync(
        RemoteTunnelStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            lock (_lock)
            {
                if (IsProcessRunning())
                {
                    return new RemoteTunnelStartResult
                    {
                        Success = true,
                        PublicUrl = _publicUrl
                    };
                }

                _publicUrl = null;
                _errorMessage = null;
                _startedAtUtc = null;
            }

            string executablePath;
            try
            {
                executablePath = await ResolveExecutablePathAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "cloudflared could not be prepared.");
                return SetError(ex.Message);
            }

            var urlSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.ArgumentList.Add("tunnel");
            process.StartInfo.ArgumentList.Add("--url");
            process.StartInfo.ArgumentList.Add(request.LocalUrl);

            process.Exited += (_, _) =>
            {
                if (!urlSource.Task.IsCompleted)
                {
                    urlSource.TrySetException(new InvalidOperationException("cloudflared exited before publishing a Quick Tunnel URL."));
                }
            };

            try
            {
                if (!process.Start())
                {
                    return SetError("Could not start cloudflared.");
                }
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "cloudflared executable could not be started.");
                return SetError("cloudflared could not be started. Check if PocketMC has permission to run executables.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "cloudflared failed to start.");
                return SetError($"cloudflared failed to start: {ex.Message}");
            }

            lock (_lock)
            {
                _process = process;
                _startedAtUtc = DateTimeOffset.UtcNow;
            }

            try
            {
                _jobObject.AddProcess(process.Handle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assign cloudflared to the app job object.");
            }

            _ = Task.Run(() => ReadStreamAsync(process.StandardOutput, urlSource), CancellationToken.None);
            _ = Task.Run(() => ReadStreamAsync(process.StandardError, urlSource), CancellationToken.None);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                using (timeoutCts.Token.Register(() => urlSource.TrySetCanceled(timeoutCts.Token)))
                {
                    string publicUrl = await urlSource.Task;
                    lock (_lock)
                    {
                        _publicUrl = publicUrl;
                        _errorMessage = null;
                    }

                    return new RemoteTunnelStartResult
                    {
                        Success = true,
                        PublicUrl = publicUrl
                    };
                }
            }
            catch (OperationCanceledException)
            {
                await StopAsync(CancellationToken.None);
                return SetError("Timed out waiting for cloudflared to publish a Quick Tunnel URL.");
            }
            catch (Exception ex)
            {
                await StopAsync(CancellationToken.None);
                return SetError(ex.Message);
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (_lock)
        {
            process = _process;
            _process = null;
            _publicUrl = null;
            _startedAtUtc = null;
        }

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error while stopping cloudflared.");
        }
        finally
        {
            process.Dispose();
        }
    }

    public RemoteTunnelStatus GetStatus()
    {
        lock (_lock)
        {
            return new RemoteTunnelStatus
            {
                IsRunning = IsProcessRunning(),
                PublicUrl = _publicUrl,
                ErrorMessage = _errorMessage,
                StartedAtUtc = _startedAtUtc
            };
        }
    }

    private async Task ReadStreamAsync(StreamReader reader, TaskCompletionSource<string> urlSource)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (TryParsePublicUrl(line, out string? publicUrl) && publicUrl != null)
                {
                    urlSource.TrySetResult(publicUrl);
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or IOException)
        {
            _logger.LogDebug(ex, "cloudflared output stream closed.");
        }
    }

    private async Task<string> ResolveExecutablePathAsync(CancellationToken cancellationToken)
    {
        return await _installer.EnsureInstalledAsync(cancellationToken);
    }

    private bool IsProcessRunning() => _process != null && !_process.HasExited;

    private RemoteTunnelStartResult SetError(string message)
    {
        lock (_lock)
        {
            _errorMessage = message;
            _publicUrl = null;
            _startedAtUtc = null;
        }

        return RemoteTunnelStartResult.Failed(message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown cleanup is best effort.
        }
    }
}
