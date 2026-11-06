using ItmoBot.Application.Mcp;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration.Mcp;

public sealed class McpRuntimeIntegrationTests
{
    [Fact]
    public async Task InitializeAsync_Runtime_OwnsRealStdioConnectionUntilDisposal()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var settings = new McpTestSettings().Create();
        var transport = new CapturingMcpTransport(new StdioMcpTransportFactory().Create(settings));
        var client = new StdioMcpClient(settings, new FixedMcpTransportFactory(transport),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
        await using var runtime = new McpRuntime(client, NullLogger.Instance);

        // Act
        var initialized = await runtime.InitializeAsync(timeout.Token);
        var catalog = await runtime.GetCatalogAsync(timeout.Token);
        await runtime.DisposeAsync();

        // Assert
        Assert.IsType<Result<McpCatalog>.Success>(initialized);
        Assert.Same(initialized, catalog);
        var discovered = Assert.IsType<Result<McpCatalog>.Success>(catalog).Value;
        Assert.Equal(new[] { "add_reminder", "get_schedule", "get_weather" },
            discovered.Tools.Select(tool => tool.Name).OrderBy(name => name));
        Assert.Equal("schedule://current-week", Assert.Single(discovered.Resources).Uri);
        Assert.NotNull(transport.Connection);
        Assert.True(transport.Connection.MessageReader.Completion.IsCompleted);
    }
}
