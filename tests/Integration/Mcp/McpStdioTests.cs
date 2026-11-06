using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Integration.Mcp;

public sealed class McpStdioTests
{
    [Fact]
    public async Task StartServer_StdioClient_InitializesAnswersPingAndExitsOnClosedInput()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new McpServerProcess();

        // Act
        using var initialization = await server.InitializeAsync(timeout.Token);
        await server.SendAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "ping"
        }, timeout.Token);
        using var ping = await server.ReadAsync(timeout.Token);
        var shutdown = await server.CloseInputAsync(timeout.Token);

        // Assert
        Assert.Equal("2.0", initialization.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, initialization.RootElement.GetProperty("id").GetInt32());
        var result = initialization.RootElement.GetProperty("result");
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("protocolVersion").GetString()));
        Assert.Equal("ItmoBot.Mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(2, ping.RootElement.GetProperty("id").GetInt32());
        Assert.Empty(ping.RootElement.GetProperty("result").EnumerateObject());
        Assert.Equal(0, shutdown.ExitCode);
        Assert.Empty(shutdown.RemainingOutput);
    }
}
