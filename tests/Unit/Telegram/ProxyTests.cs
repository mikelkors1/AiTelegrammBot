using System.Net;
using System.Net.Sockets;
using System.Text;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Telegram;

public sealed class ProxyTests
{
    [Fact]
    public async Task HttpProxyReceivesConnectAndHasNoDirectFallback()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var values = new TestValues().Environment();
        values["TELEGRAM_PROXY_URL"] = $"http://127.0.0.1:{port}";
        using var client = new TelegramHttpClientFactory().Create(new TestValues().Settings(values));
        var request = client.GetAsync("https://api.telegram.org", timeout.Token);
        using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
        var stream = socket.GetStream();
        var header = new StringBuilder();
        var buffer = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {

            // Act
            var actualResult = await stream.ReadAsync(buffer, timeout.Token);

            // Assert
            Assert.Equal(1, actualResult);
            header.Append((char)buffer[0]);
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
        socket.Close();
        await Assert.ThrowsAsync<HttpRequestException>(async () => await request);
        Assert.StartsWith("CONNECT api.telegram.org:443 HTTP/1.1\r\n", header.ToString());
    }

}
