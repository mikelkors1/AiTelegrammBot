using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Protocol;
using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;
using ItmoBot.Mcp.Schedule;
using ItmoBot.Tests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ItmoBot.Tests.Unit.Schedule;

public sealed class ScheduleEndpointTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"date\":null}")]
    [InlineData("{\"date\":42}")]
    [InlineData("{\"date\":\"2026-02-30\"}")]
    [InlineData("{\"date\":\"2026-10-12\",\"owner_id\":42}")]
    public async Task CallAsync_InvalidArguments_AreRejectedBeforeReadingSource(string json)
    {
        // Arrange
        var source = new FixedScheduleSource(new ScheduleResult<ScheduleSnapshot>.Success(new ScheduleSnapshot(
            new Dictionary<DateOnly, IReadOnlyList<Lesson>>())));
        var zone = new UserTimeZone("Europe/Moscow");
        var endpoint = new ScheduleMcpEndpoint(new ScheduleService(source, zone, new ManualTimeProvider()),
            zone);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        // Act
        var result = await endpoint.CallAsync(new CallToolRequestParams { Name = "get_schedule", Arguments = arguments }, CancellationToken.None);

        // Assert
        Assert.Equal(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal("invalid_arguments", result.StructuredContent.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, source.LoadCalls);
    }

    [Fact]
    public async Task ReadAsync_WeeklyResource_UsesFixedTimeAndSourceWithoutExternalCalls()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        clock.Advance(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero) - DateTimeOffset.UnixEpoch);
        var source = new FixedScheduleSource(new ScheduleResult<ScheduleSnapshot>.Success(new ScheduleSnapshot(
            new Dictionary<DateOnly, IReadOnlyList<Lesson>>())));
        var zone = new UserTimeZone("Europe/Moscow");
        var endpoint = new ScheduleMcpEndpoint(new ScheduleService(source, zone, clock),
            zone);

        // Act
        var result = await endpoint.ReadAsync(new ReadResourceRequestParams { Uri = "schedule://current-week" }, CancellationToken.None);

        // Assert
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        using var document = JsonDocument.Parse(content.Text);
        Assert.Equal("2025-12-29", document.RootElement.GetProperty("week_start").GetString());
        Assert.Equal("2026-01-04", document.RootElement.GetProperty("week_end").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("days").GetArrayLength());
        Assert.Equal(1, source.LoadCalls);
    }
}
