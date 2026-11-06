using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Configuration;

public sealed class McpSettingsTests
{
    [Fact]
    public void Load_Defaults_PreserveExistingBotConfiguration()
    {
        // Arrange
        var values = new TestValues();

        // Act
        var settings = values.Settings().Mcp;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(15), settings.Timeout);
        Assert.Equal("Europe/Moscow", settings.Schedule.TimeZone);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.WeatherHttp.Timeout);
        Assert.Equal(2, settings.WeatherHttp.Attempts);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(settings.ServerPath)!, "Data", "schedule.json"), settings.Schedule.SourcePath);
        Assert.Equal(Path.GetFullPath(Path.Combine("mcp", "bin", "Debug", "net10.0", "ItmoBot.Mcp.dll")), settings.ServerPath);
    }

    [Fact]
    public void Load_CustomMcpSettings_PreservesPathTimeoutAndWeatherSettings()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), "custom-mcp.dll");
        var schedulePath = Path.Combine(Path.GetTempPath(), "custom-schedule.json");

        // Act
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["MCP_SERVER_PATH"] = path,
            ["MCP_TIMEOUT_SECONDS"] = "3",
            ["SCHEDULE_FILE"] = schedulePath,
            ["SCHEDULE_TIMEZONE"] = "Europe/Berlin",
            ["WEATHER_HTTP_TIMEOUT_SECONDS"] = "2",
            ["WEATHER_HTTP_MAX_ATTEMPTS"] = "1",
        }).Mcp;

        // Assert
        Assert.Equal(path, settings.ServerPath);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.Timeout);
        Assert.Equal(schedulePath, settings.Schedule.SourcePath);
        Assert.Equal("Europe/Berlin", settings.Schedule.TimeZone);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.WeatherHttp.Timeout);
        Assert.Equal(1, settings.WeatherHttp.Attempts);
    }


}
