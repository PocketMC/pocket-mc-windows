using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Features.RemoteControl.Hosting;

public sealed class RemoteConsoleWebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServerLifecycleService _lifecycleService;

    public RemoteConsoleWebSocketHandler(IServerLifecycleService lifecycleService)
    {
        _lifecycleService = lifecycleService;
    }

    public async Task HandleAsync(HttpContext context, Guid instanceId)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Expected a WebSocket request.");
            return;
        }

        var process = _lifecycleService.GetProcess(instanceId);
        if (process == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Instance is not running.");
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        using var sendLock = new SemaphoreSlim(1, 1);

        foreach (string line in process.OutputBuffer.ToArray())
        {
            await SendLineAsync(socket, sendLock, "history", line, context.RequestAborted);
        }

        void OnOutput(string line) => _ = SendLineAsync(socket, sendLock, "stdout", line, CancellationToken.None);
        void OnError(string line) => _ = SendLineAsync(socket, sendLock, "stderr", line, CancellationToken.None);

        process.OnOutputLine += OnOutput;
        process.OnErrorLine += OnError;

        try
        {
            byte[] buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.CloseStatus.HasValue)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            process.OnOutputLine -= OnOutput;
            process.OnErrorLine -= OnError;

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
    }

    private static async Task SendLineAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string type,
        string line,
        CancellationToken cancellationToken)
    {
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    type,
                    line,
                    timestampUtc = DateTimeOffset.UtcNow
                },
                JsonOptions);

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }
        catch
        {
            // Ignore any socket/task cancellation exceptions to prevent unobserved task failures
        }
    }
}
