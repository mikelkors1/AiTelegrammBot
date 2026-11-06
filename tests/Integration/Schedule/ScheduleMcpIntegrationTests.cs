using System.Globalization;
using System.Text.Json;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using Xunit;

namespace ItmoBot.Tests.Integration.Schedule;

public sealed class ScheduleMcpIntegrationTests
{
    [Fact]
    public async Task CallScheduleTool_ConfiguredSourceAndTimezone_ReachChildProcess()
    {
        // Arrange
        using var file = new TemporaryScheduleFile("""
            {"days":[{"date":"2026-10-12","lessons":[{"start":"15:00","end":"16:00","title":"Тестовая тема","location":"Тестовая аудитория"}]}]}
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await ConnectAsync(file.Path, timeout.Token, "Asia/Tokyo");

        // Act
        var result = await client.CallToolAsync("get_schedule", new Dictionary<string, object?> { ["date"] = "2026-10-12" }, cancellationToken: timeout.Token);

        // Assert
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal("Asia/Tokyo", result.StructuredContent.Value.GetProperty("timezone").GetString());
        var lesson = Assert.Single(result.StructuredContent.Value.GetProperty("lessons").EnumerateArray());
        Assert.Equal("Тестовая тема", lesson.GetProperty("title").GetString());
        Assert.Equal("15:00", lesson.GetProperty("start").GetString());
    }

    [Fact]
    public async Task ReadResourceAsync_UnavailableSource_MakesResourceReadFail()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var path = Path.Combine(Path.GetTempPath(), $"missing-schedule-{Guid.NewGuid():N}.json");
        await using var client = await ConnectAsync(path, timeout.Token);

        // Act
        var running = client.ReadResourceAsync("schedule://current-week", cancellationToken: timeout.Token).AsTask();

        // Assert
        var error = await Assert.ThrowsAnyAsync<McpException>(() => running);
        Assert.DoesNotContain(path, error.Message);
    }

    [Fact]
    public async Task CallScheduleTool_IndependentClient_DiscoversAndCallsScheduleTool()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await ConnectAsync(null, timeout.Token);

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("get_schedule", new Dictionary<string, object?> { ["date"] = "2026-10-12" }, cancellationToken: timeout.Token);

        // Assert
        Assert.Equal(3, tools.Count);
        var tool = Assert.Single(tools, item => item.ProtocolTool.Name == "get_schedule").ProtocolTool;
        Assert.Equal("get_schedule", tool.Name);
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.NotNull(tool.OutputSchema);
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var value = result.StructuredContent.Value;
        Assert.Equal("2026-10-12", value.GetProperty("date").GetString());
        Assert.Equal("Europe/Moscow", value.GetProperty("timezone").GetString());
        Assert.Equal("repository_schedule", value.GetProperty("source").GetString());
        var lesson = Assert.Single(value.GetProperty("lessons").EnumerateArray());
        Assert.Equal("09:00", lesson.GetProperty("start").GetString());
        Assert.Equal("10:30", lesson.GetProperty("end").GetString());
        Assert.Equal("Алгоритмы", lesson.GetProperty("title").GetString());
        Assert.Equal("Учебная аудитория A", lesson.GetProperty("location").GetString());
    }

    [Fact]
    public async Task CallScheduleTool_EmptyDate_ReturnsSuccessfulEmptyLessons()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await ConnectAsync(null, timeout.Token);

        // Act
        var result = await client.CallToolAsync("get_schedule", new Dictionary<string, object?> { ["date"] = "2026-10-18" }, cancellationToken: timeout.Token);

        // Assert
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal("2026-10-18", result.StructuredContent.Value.GetProperty("date").GetString());
        Assert.Empty(result.StructuredContent.Value.GetProperty("lessons").EnumerateArray());
    }

    [Theory]
    [InlineData("2026-02-30", false)]
    [InlineData("12.10.2026", false)]
    [InlineData("2026-10-12", true)]
    public async Task CallScheduleTool_InvalidDatesAndInjectedFilePaths_AreRejected(string date, bool addPath)
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await ConnectAsync(null, timeout.Token);
        var arguments = new Dictionary<string, object?> { ["date"] = date };
        if (addPath)
        {
            arguments["path"] = "/private/untrusted-file.json";
        }

        // Act
        var result = await client.CallToolAsync("get_schedule", arguments, cancellationToken: timeout.Token);

        // Assert
        Assert.Equal(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal("error", result.StructuredContent.Value.GetProperty("status").GetString());
        Assert.Equal("invalid_arguments", result.StructuredContent.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("untrusted-file", result.StructuredContent.Value.GetRawText());
    }

    [Fact]
    public async Task CallScheduleTool_MissingSource_IsStructuredErrorInsteadOfEmptyLessons()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var path = Path.Combine(Path.GetTempPath(), $"missing-schedule-{Guid.NewGuid():N}.json");
        await using var client = await ConnectAsync(path, timeout.Token);

        // Act
        var result = await client.CallToolAsync("get_schedule", new Dictionary<string, object?> { ["date"] = "2026-10-12" }, cancellationToken: timeout.Token);

        // Assert
        Assert.Equal(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var value = result.StructuredContent.Value;
        Assert.Equal("source_unavailable", value.GetProperty("error").GetProperty("code").GetString());
        Assert.False(value.TryGetProperty("lessons", out _));
        Assert.DoesNotContain(path, value.GetRawText());
    }

    [Fact]
    public async Task ReadResourceAsync_IndependentClient_ReadsWeeklyJsonResource()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await ConnectAsync(null, timeout.Token);

        // Act
        var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
        var result = await client.ReadResourceAsync("schedule://current-week", cancellationToken: timeout.Token);

        // Assert
        Assert.Equal("schedule://current-week", Assert.Single(resources).Uri);
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("application/json", content.MimeType);
        using var document = JsonDocument.Parse(content.Text);
        var week = document.RootElement;
        Assert.Equal("Europe/Moscow", week.GetProperty("timezone").GetString());
        Assert.Equal("repository_schedule", week.GetProperty("source").GetString());
        Assert.Equal(7, week.GetProperty("days").GetArrayLength());
        var start = DateOnly.ParseExact(week.GetProperty("week_start").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(week.GetProperty("week_end").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(start.AddDays(6), end);
        Assert.Equal(week.GetProperty("week_start").GetString(), week.GetProperty("days")[0].GetProperty("date").GetString());
        Assert.Equal(week.GetProperty("week_end").GetString(), week.GetProperty("days")[6].GetProperty("date").GetString());
    }

    [Fact]
    public async Task CallScheduleTool_UnknownToolAndResource_AreRejected()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await ConnectAsync(null, timeout.Token);

        // Act
        var tool = client.CallToolAsync("unknown_tool", cancellationToken: timeout.Token).AsTask();
        var resource = client.ReadResourceAsync("schedule://unknown", cancellationToken: timeout.Token).AsTask();

        // Assert
        await Assert.ThrowsAnyAsync<McpException>(() => tool);
        await Assert.ThrowsAnyAsync<McpException>(() => resource);
    }

    private async Task<McpClient> ConnectAsync(string? sourcePath, CancellationToken cancellationToken, string timezone = "Europe/Moscow")
    {
        var settings = new McpTestSettings().Create(scheduleFile: sourcePath, timezone: timezone);
        var transport = new StdioMcpTransportFactory().Create(settings);
        return await McpClient.CreateAsync(transport, new McpClientOptions
        {
            InitializationTimeout = TimeSpan.FromSeconds(15),
        }, NullLoggerFactory.Instance, cancellationToken);
    }
}
