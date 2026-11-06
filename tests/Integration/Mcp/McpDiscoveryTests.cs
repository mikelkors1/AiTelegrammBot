using System.Text.Json;
using ItmoBot.Application.Mcp;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OperationResult = ItmoBot.Shared.Results.Result;
using Xunit;

namespace ItmoBot.Tests.Integration.Mcp;

public sealed class McpDiscoveryTests
{

    [Fact]
    public async Task DiscoverAsync_PaginatedCatalog_CollectsToolsSchemasHintsAndResources()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var toolRequests = 0;
        var resourceRequests = 0;
        var options = Options();
        options.Handlers.ListToolsHandler = (context, _) =>
        {
            toolRequests++;
            return ValueTask.FromResult(context.Params?.Cursor is null
                ? new ListToolsResult { Tools = [Tool("fixture_weather", true)], NextCursor = "more-tools" }
                : new ListToolsResult { Tools = [Tool("fixture_reminder", false)] });
        };
        options.Handlers.ListResourcesHandler = (context, _) =>
        {
            resourceRequests++;
            return ValueTask.FromResult(context.Params?.Cursor is null
                ? new ListResourcesResult { Resources = [Resource("schedule://current-week")], NextCursor = "more-resources" }
                : new ListResourcesResult { Resources = [Resource("schedule://next-week")] });
        };
        await using var server = new InMemoryMcpServer(options);
        Result<IMcpSession> connected = await Client(server.ClientTransport).ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;

        // Act
        var result = await session.DiscoverAsync(timeout.Token);

        // Assert
        var catalog = Assert.IsType<Result<McpCatalog>.Success>(result).Value;
        Assert.Equal(new[] { "fixture_weather", "fixture_reminder" }, catalog.Tools.Select(tool => tool.Name));
        Assert.Equal("Fixture tool", catalog.Tools[0].Description);
        Assert.True(catalog.Tools[0].ReadOnlyHint);
        Assert.False(catalog.Tools[1].ReadOnlyHint);
        var input = catalog.Tools[0].InputSchema;
        Assert.Equal("string", input.GetProperty("properties").GetProperty("city").GetProperty("type").GetString());
        Assert.Equal("city", input.GetProperty("required")[0].GetString());
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("number", catalog.Tools[0].OutputSchema.GetProperty("properties").GetProperty("temperature_c").GetProperty("type").GetString());
        Assert.Equal(new[] { "schedule://current-week", "schedule://next-week" }, catalog.Resources.Select(resource => resource.Uri));
        Assert.All(catalog.Resources, resource => Assert.Equal("application/json", resource.MimeType));
        Assert.Equal(2, toolRequests);
        Assert.Equal(2, resourceRequests);
    }

    [Theory]
    [InlineData("missing-output")]
    [InlineData("duplicate-tool")]
    [InlineData("duplicate-resource")]
    [InlineData("invalid-uri")]
    [InlineData("repeated-cursor")]
    public async Task DiscoverAsync_InvalidCatalog_ReturnsSafeFailure(string scenario)
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tool = Tool("fixture_tool", true);
        if (scenario == "missing-output")
        {
            tool.OutputSchema = null;
        }

        var options = Options();
        options.Handlers.ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
        {
            Tools = scenario switch
            {
                "duplicate-tool" => [tool, tool],
                "repeated-cursor" => [],
                _ => [tool],
            },
            NextCursor = scenario == "repeated-cursor" ? "same-cursor" : null,
        });
        options.Handlers.ListResourcesHandler = (_, _) => ValueTask.FromResult(new ListResourcesResult
        {
            Resources = scenario switch
            {
                "duplicate-resource" => [Resource("schedule://current-week"), Resource("schedule://current-week")],
                "invalid-uri" => [Resource("relative/private-path")],
                _ => [],
            },
        });
        await using var server = new InMemoryMcpServer(options);
        Result<IMcpSession> connected = await Client(server.ClientTransport).ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;

        // Act
        var result = await session.DiscoverAsync(timeout.Token);

        // Assert
        var error = Assert.IsType<Result<McpCatalog>.Failure>(result).Error;
        Assert.Equal(ErrorCode.InvalidMcpResponse, error.Code);
        Assert.DoesNotContain("private-path", error.Message);
        Assert.DoesNotContain("fixture_tool", error.Message);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoveryTimeout_UsesReplaceableClock()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options();
        options.Handlers.ListToolsHandler = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ListToolsResult { Tools = [] };
        };
        await using var server = new InMemoryMcpServer(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Result<IMcpSession> connected = await Client(server.ClientTransport, clock).ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;
        var running = session.DiscoverAsync(timeout.Token);
        await started.Task.WaitAsync(timeout.Token);

        // Act
        clock.Advance(TimeSpan.FromSeconds(15));
        var result = await running.WaitAsync(timeout.Token);

        // Assert
        Assert.Equal(ErrorCode.McpTimeout, Assert.IsType<Result<McpCatalog>.Failure>(result).Error.Code);
    }

    [Fact]
    public async Task DiscoverAsync_CancellationDuringDiscovery_PropagatesAndSessionRemainsUsable()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options();
        options.Handlers.ListToolsHandler = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ListToolsResult { Tools = [] };
        };
        await using var server = new InMemoryMcpServer(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = new CancellationTokenSource();
        Result<IMcpSession> connected = await Client(server.ClientTransport).ConnectAsync(timeout.Token);
        await using var session = Assert.IsType<Result<IMcpSession>.Success>(connected).Value;
        var running = session.DiscoverAsync(cancellation.Token);
        await started.Task.WaitAsync(timeout.Token);

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(timeout.Token));
        Assert.IsType<OperationResult.Success>(await session.CheckConnectionAsync(timeout.Token));
    }

    private StdioMcpClient Client(IClientTransport transport, TimeProvider? clock = null)
    {
        return new StdioMcpClient(new McpTestSettings().Create(), new FixedMcpTransportFactory(transport),
            new McpErrorMapper(), clock ?? TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
    }

    private McpServerOptions Options()
    {
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "FixtureServer", Version = "1.0.0" },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability(), Resources = new ResourcesCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] }),
                ListResourcesHandler = (_, _) => ValueTask.FromResult(new ListResourcesResult { Resources = [] }),
            },
        };
    }

    private Tool Tool(string name, bool readOnly)
    {
        return new Tool
        {
            Name = name,
            Description = "Fixture tool",
            InputSchema = Schema("""{"type":"object","properties":{"city":{"type":"string"}},"required":["city"],"additionalProperties":false}"""),
            OutputSchema = Schema("""{"type":"object","properties":{"temperature_c":{"type":"number"}},"required":["temperature_c"],"additionalProperties":false}"""),
            Annotations = new ToolAnnotations { ReadOnlyHint = readOnly },
        };
    }

    private Resource Resource(string uri)
    {
        return new Resource { Uri = uri, Name = "Fixture schedule", Description = "Fixture resource", MimeType = "application/json" };
    }

    private JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
