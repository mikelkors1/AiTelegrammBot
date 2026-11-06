using ItmoBot.Application.Mcp;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Mcp;

public sealed class McpClientTests
{
    [Fact]
    public async Task ConnectAsync_ConnectionFailure_ReturnsSafeErrorWithoutRetryOrSensitiveLogs()
    {
        // Arrange
        var transport = new StubMcpTransport(_ => throw new IOException("token=secret; private-path"));
        var factory = new FixedMcpTransportFactory(transport);
        var logger = new RecordingLogger();
        var client = new StdioMcpClient(new McpTestSettings().Create(), factory, new McpErrorMapper(), TimeProvider.System, logger,
            new McpCatalogReader(new McpCatalogMapper()));

        // Act
        Result<IMcpSession> result = await client.ConnectAsync(CancellationToken.None);

        // Assert
        var error = Assert.IsType<Result<IMcpSession>.Failure>(result).Error;
        Assert.Equal(ErrorCode.McpUnavailable, error.Code);
        Assert.True(error.IsRetryable);
        Assert.Equal(1, factory.CreateCalls);
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("private-path", error.Message);
        Assert.DoesNotContain("secret", string.Join('\n', logger.Entries));
        Assert.DoesNotContain("private-path", string.Join('\n', logger.Entries));
    }

    [Fact]
    public async Task ConnectAsync_ConnectionTimeout_ReturnsFailureUsingReplaceableClock()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var transport = new StubMcpTransport(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        var factory = new FixedMcpTransportFactory(transport);
        var client = new StdioMcpClient(new McpTestSettings().Create(), factory, new McpErrorMapper(), clock, new RecordingLogger(),
            new McpCatalogReader(new McpCatalogMapper()));
        var running = client.ConnectAsync(CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(15));

        // Act
        clock.Advance(TimeSpan.FromSeconds(15));
        Result<IMcpSession> result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        var error = Assert.IsType<Result<IMcpSession>.Failure>(result).Error;
        Assert.Equal(ErrorCode.McpTimeout, error.Code);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task ConnectAsync_CallerCancellationDuringConnection_Propagates()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new StubMcpTransport(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        var factory = new FixedMcpTransportFactory(transport);
        var client = new StdioMcpClient(new McpTestSettings().Create(), factory, new McpErrorMapper(), TimeProvider.System, new RecordingLogger(),
            new McpCatalogReader(new McpCatalogMapper()));
        using var cancellation = new CancellationTokenSource();
        var running = client.ConnectAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task ConnectAsync_AlreadyCancelledRequest_DoesNotCreateTransport()
    {
        // Arrange
        var transport = new StubMcpTransport(_ => throw new InvalidOperationException("Must not connect"));
        var factory = new FixedMcpTransportFactory(transport);
        var client = new StdioMcpClient(new McpTestSettings().Create(), factory, new McpErrorMapper(), TimeProvider.System, new RecordingLogger(),
            new McpCatalogReader(new McpCatalogMapper()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var running = client.ConnectAsync(cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public void ConnectAsync_InvalidProtocolResponse_MapsToSeparateErrorCode()
    {
        // Arrange
        var error = new System.Text.Json.JsonException("private-response");

        // Act
        var result = new McpErrorMapper().Map(error);

        // Assert
        Assert.Equal(ErrorCode.InvalidMcpResponse, result.Code);
        Assert.False(result.IsRetryable);
        Assert.DoesNotContain("private-response", result.Message);
    }
}
