using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Unit.Mcp;

public sealed class McpRuntimeTests
{
    [Fact]
    public async Task InitializeAsync_Initialization_IsIdempotentAndSessionIsDisposedOnce()
    {
        // Arrange
        var session = new FakeMcpSession();
        var client = new FakeMcpClient { Result = new Result<IMcpSession>.Success(session) };
        await using var runtime = new McpRuntime(client, NullLogger.Instance);

        // Act
        var initialized = await runtime.InitializeAsync(CancellationToken.None);
        var repeated = await runtime.InitializeAsync(CancellationToken.None);
        var catalog = await runtime.GetCatalogAsync(CancellationToken.None);
        await runtime.DisposeAsync();
        await runtime.DisposeAsync();

        // Assert
        Assert.IsType<Result<McpCatalog>.Success>(initialized);
        Assert.Same(initialized, repeated);
        Assert.Same(initialized, catalog);
        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal(1, session.DiscoveryCalls);
        Assert.Equal(1, session.CheckCalls);
        Assert.Equal(1, session.DisposeCalls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.GetCatalogAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InitializeAsync_FailedConnection_IsStoredWithoutAutomaticRetry()
    {
        // Arrange
        var client = new FakeMcpClient();
        await using var runtime = new McpRuntime(client, NullLogger.Instance);

        // Act
        var initialized = await runtime.InitializeAsync(CancellationToken.None);
        var catalog = await runtime.GetCatalogAsync(CancellationToken.None);
        await runtime.InitializeAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.McpUnavailable, Assert.IsType<Result<McpCatalog>.Failure>(initialized).Error.Code);
        Assert.Same(initialized, catalog);
        Assert.Equal(1, client.ConnectCalls);
    }

    [Fact]
    public async Task InitializeAsync_FailedDiscovery_ClosesSessionAndPreservesErrorCode()
    {
        // Arrange
        var session = new FakeMcpSession
        {
            DiscoveryResult = new Result<McpCatalog>.Failure(new AppError(ErrorCode.InvalidMcpResponse, "Некорректный каталог.")),
        };
        var client = new FakeMcpClient { Result = new Result<IMcpSession>.Success(session) };
        await using var runtime = new McpRuntime(client, NullLogger.Instance);

        // Act
        var result = await runtime.InitializeAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.InvalidMcpResponse, Assert.IsType<Result<McpCatalog>.Failure>(result).Error.Code);
        Assert.Equal(1, session.DisposeCalls);
        Assert.Same(result, await runtime.GetCatalogAsync(CancellationToken.None));
        Assert.Equal(0, session.CheckCalls);
    }

    [Fact]
    public async Task InitializeAsync_CancelledDiscovery_ClosesSessionAndDoesNotConvertCancellationToFailure()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeMcpSession
        {
            OnDiscover = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new Result<McpCatalog>.Success(new ItmoBot.Application.Mcp.McpCatalog([], []));
            },
        };
        var client = new FakeMcpClient { Result = new Result<IMcpSession>.Success(session) };
        await using var runtime = new McpRuntime(client, NullLogger.Instance);
        using var cancellation = new CancellationTokenSource();
        var running = runtime.InitializeAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, session.DisposeCalls);
        var next = new FakeMcpSession();
        client.Result = new Result<IMcpSession>.Success(next);
        Assert.IsType<Result<McpCatalog>.Success>(await runtime.InitializeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCatalogAsync_LostConnection_InvalidatesCatalogAndClosesSession()
    {
        // Arrange
        var session = new FakeMcpSession();
        var client = new FakeMcpClient { Result = new Result<IMcpSession>.Success(session) };
        await using var runtime = new McpRuntime(client, NullLogger.Instance);
        await runtime.InitializeAsync(CancellationToken.None);
        session.CheckResult = new Result.Failure(new AppError(ErrorCode.McpUnavailable, "Связь потеряна."));

        // Act
        var catalog = await runtime.GetCatalogAsync(CancellationToken.None);
        var repeated = await runtime.GetCatalogAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.McpUnavailable, Assert.IsType<Result<McpCatalog>.Failure>(catalog).Error.Code);
        Assert.Same(catalog, repeated);
        Assert.Equal(1, session.CheckCalls);
        Assert.Equal(1, session.DisposeCalls);
        Assert.Equal(1, client.ConnectCalls);
    }

    [Fact]
    public async Task InitializeAsync_UnexpectedConnectionException_DoesNotLeakItsMessage()
    {
        // Arrange
        var client = new FakeMcpClient { OnConnect = _ => throw new IOException("secret-token; user-text") };
        var logger = new RecordingLogger();
        await using var runtime = new McpRuntime(client, logger);

        // Act
        var result = await runtime.InitializeAsync(CancellationToken.None);

        // Assert
        var error = Assert.IsType<Result<McpCatalog>.Failure>(result).Error;
        Assert.Equal(ErrorCode.McpUnavailable, error.Code);
        Assert.DoesNotContain("secret-token", error.Message);
        Assert.DoesNotContain("user-text", string.Join('\n', logger.Entries));
        Assert.DoesNotContain("secret-token", string.Join('\n', logger.Entries));
    }
}
