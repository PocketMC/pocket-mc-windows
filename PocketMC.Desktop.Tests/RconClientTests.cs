using System.Net;
using System.Net.Sockets;
using PocketMC.Desktop.Infrastructure.Process;

namespace PocketMC.Desktop.Tests;

public sealed class RconClientTests
{
    [Fact]
    public async Task ConnectAsync_RejectsOversizedResponseLength()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = accepted.GetStream();

            byte[] requestLengthBuffer = new byte[4];
            await ReadExactAsync(stream, requestLengthBuffer);
            int requestLength = BitConverter.ToInt32(requestLengthBuffer, 0);
            byte[] requestBody = new byte[requestLength];
            await ReadExactAsync(stream, requestBody);

            byte[] maliciousLength = BitConverter.GetBytes(1024 * 1024 + 1);
            await stream.WriteAsync(maliciousLength);
        });

        using var client = new RconClient("127.0.0.1", port, "password");

        await Assert.ThrowsAsync<InvalidDataException>(() => client.ConnectAsync());
        await serverTask;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}
