using ItmoBot.Application.Mcp;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration.Mcp;

public sealed class McpClientIntegrationTests
{
    [Fact]
    public async Task ConnectAsync_LostTransport_ReturnsUnavailableInsteadOfSuccess()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var settings = new McpTestSettings().Create();
        var transport = new CapturingMcpTransport(new StdioMcpTransportFactory().Create(settings));
        var client = new StdioMcpClient(settings, new FixedMcpTransportFactory(transport),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
        Result<IMcpSession> connected = await client.ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;

        // Act
        Assert.NotNull(transport.Connection);
        await transport.Connection.DisposeAsync();
        var result = await session.CheckConnectionAsync(timeout.Token);
        var discovery = await session.DiscoverAsync(timeout.Token);

        // Assert
        Assert.Equal(ErrorCode.McpUnavailable, Assert.IsType<Result.Failure>(result).Error.Code);
        Assert.Equal(ErrorCode.McpUnavailable, Assert.IsType<Result<McpCatalog>.Failure>(discovery).Error.Code);
    }

    [Fact]
    public async Task ConnectAsync_CallerCancellation_DoesNotCloseSession()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var client = new StdioMcpClient(new McpTestSettings().Create(), new StdioMcpTransportFactory(),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
        Result<IMcpSession> connected = await client.ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act
        var cancelledRequest = session.CheckConnectionAsync(cancelled.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledRequest);
        Assert.IsType<Result.Success>(await session.CheckConnectionAsync(timeout.Token));
    }

    [Fact]
    public async Task ConnectAsync_AdapterConnectsToRealServerAndSession_CanBeDisposedRepeatedly()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        IMcpClient client = new StdioMcpClient(new McpTestSettings().Create(), new StdioMcpTransportFactory(),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));

        // Act
        Result<IMcpSession> connected = await client.ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;
        var checkedConnection = await session.CheckConnectionAsync(timeout.Token);
        await session.DisposeAsync();
        await session.DisposeAsync();
        var afterDisposal = await session.CheckConnectionAsync(timeout.Token);

        // Assert
        Assert.IsType<Result.Success>(checkedConnection);
        Assert.Equal(ErrorCode.McpUnavailable, Assert.IsType<Result.Failure>(afterDisposal).Error.Code);
    }

    [Fact]
    public async Task ConnectAsync_MissingServer_ReturnsFailureWithoutExposingPath()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var missing = Path.Combine(Path.GetTempPath(), $"missing-mcp-{Guid.NewGuid():N}.dll");
        IMcpClient client = new StdioMcpClient(new McpTestSettings().Create(missing), new StdioMcpTransportFactory(),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));

        // Act
        Result<IMcpSession> result = await client.ConnectAsync(timeout.Token);

        // Assert
        var error = Assert.IsType<Result<IMcpSession>.Failure>(result).Error;
        Assert.Equal(ErrorCode.McpUnavailable, error.Code);
        Assert.DoesNotContain(missing, error.Message);
    }
}
