using System.Text.Json;
using ItmoBot.Mcp.Hosting;
using ItmoBot.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Xunit;

namespace ItmoBot.Tests.Integration.Reminders;

public sealed class ReminderProtocolTests
{
    [Theory]
    [InlineData("{}", "invalid_arguments")]
    [InlineData("{\"text\":null,\"remind_at\":\"2030-01-01T12:00:00Z\"}", "invalid_arguments")]
    [InlineData("{\"text\":\"   \",\"remind_at\":\"2030-01-01T12:00:00Z\"}", "invalid_arguments")]
    [InlineData("{\"text\":\"Тест\",\"remind_at\":\"2030-01-01T12:00:00\"}", "invalid_arguments")]
    [InlineData("{\"text\":\"Тест\",\"remind_at\":\"2030-02-30T12:00:00Z\"}", "invalid_arguments")]
    [InlineData("{\"text\":\"Тест\",\"remind_at\":\"2030-01-01T12:00:00Z\",\"owner_id\":42}", "invalid_arguments")]
    [InlineData("{\"text\":\"Тест\",\"remind_at\":\"2030-01-01T12:00:00Z\"}", "unauthorized")]
    public async Task CallReminderTool_IndependentClient_CannotCreateWithoutTrustedContext(string json, string expected)
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.Configuration["MCP_SIGNING_KEY"] = "";
        builder.Configuration["MCP_POSTGRES_CONNECTION_STRING"] = "";
        new McpServerComposition().Configure(builder);
        using var host = builder.Build();
        await using var server = new InMemoryMcpServer(host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await McpClient.CreateAsync(server.ClientTransport, cancellationToken: timeout.Token);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

        // Act
        var result = await client.CallToolAsync("add_reminder", arguments, cancellationToken: timeout.Token);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(expected, result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }
}
