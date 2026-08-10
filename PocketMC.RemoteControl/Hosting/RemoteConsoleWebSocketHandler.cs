using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using PocketMC.Application.Interfaces;
using PocketMC.Application.Interfaces.Instances;

namespace PocketMC.RemoteControl.Hosting;

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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        var channel = Channel.CreateBounded<(string type, string line)>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Replay history directly to WebSocket first
        foreach (string line in process.OutputBuffer.ToArray())
        {
            channel.Writer.TryWrite(("history", line));
        }

        void OnOutput(string line) => channel.Writer.TryWrite(("stdout", line));
        void OnError(string line) => channel.Writer.TryWrite(("stderr", line));

        process.OnOutputLine += OnOutput;
        process.OnErrorLine += OnError;

        var sendTask = Task.Run(async () =>
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(cts.Token))
                {
                    while (channel.Reader.TryRead(out var msg))
                    {
                        if (socket.State != WebSocketState.Open) return;

                        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                            new
                            {
                                type = msg.type,
                                line = msg.line,
                                timestampUtc = DateTimeOffset.UtcNow
                            },
                            JsonOptions);

                        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        });

        try
        {
            byte[] buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cts.Token);
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
            channel.Writer.TryComplete();
            cts.Cancel();
            await sendTask;

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
    }
}
