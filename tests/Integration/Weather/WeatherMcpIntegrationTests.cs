using System.Net;
using System.Text.Json;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ItmoBot.Tests.Integration.Weather;

public sealed class WeatherMcpIntegrationTests
{
    [Fact]
    public async Task CallWeatherTool_IndependentClient_DiscoversBothToolsAndCallsWeatherWithMockedHttp()
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var handler = Handler(fixtures.Locations(), fixtures.Current());
        using var host = new WeatherProtocolHost(handler);
        await using var server = new InMemoryMcpServer(host.Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await McpClient.CreateAsync(server.ClientTransport, cancellationToken: timeout.Token);

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("get_weather", new Dictionary<string, object?> { ["city"] = "Тбилиси, Грузия" }, cancellationToken: timeout.Token);

        // Assert
        Assert.Equal(3, tools.Count);
        var tool = Assert.Single(tools, item => item.ProtocolTool.Name == "get_weather").ProtocolTool;
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("city", Assert.Single(tool.InputSchema.GetProperty("required").EnumerateArray()).GetString());
        Assert.NotNull(tool.OutputSchema);
        Assert.Equal(4, tool.OutputSchema.Value.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(true, tool.Annotations?.ReadOnlyHint);
        Assert.Equal(true, tool.Annotations?.OpenWorldHint);
        Assert.Equal("schedule://current-week", Assert.Single(resources).Uri);
        Assert.False(result.IsError);
        var value = result.StructuredContent!.Value;
        Assert.Equal("ok", value.GetProperty("status").GetString());
        Assert.Equal("Тбилиси", value.GetProperty("city").GetString());
        Assert.Equal("Грузия", value.GetProperty("country").GetString());
        Assert.Equal(18.5, value.GetProperty("temperature_c").GetDouble());
        Assert.Equal(3, value.GetProperty("weather_code").GetInt32());
        Assert.Equal(7.2, value.GetProperty("wind_speed").GetDouble());
        Assert.Equal("km/h", value.GetProperty("wind_unit").GetString());
        Assert.Equal("2026-10-12T10:00:00.0000000+04:00", value.GetProperty("observed_at").GetString());
        Assert.Equal("Asia/Tbilisi", value.GetProperty("timezone").GetString());
        Assert.Equal("open_meteo", value.GetProperty("source").GetString());
        Assert.Equal(value.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("countryCode=GE", handler.Requests[0].RequestUri!.Query);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"city\":null}")]
    [InlineData("{\"city\":42}")]
    [InlineData("{\"city\":\" \"}")]
    [InlineData("{\"city\":\"Тбилиси\",\"url\":\"https://untrusted.invalid\"}")]
    [InlineData("{\"city\":\"A\\nB\"}")]
    public async Task CallWeatherTool_InvalidArguments_AreRejectedWithoutHttp(string json)
    {
        // Arrange
        var handler = Handler("{}", "{}");
        using var host = new WeatherProtocolHost(handler);
        await using var server = new InMemoryMcpServer(host.Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await McpClient.CreateAsync(server.ClientTransport, cancellationToken: timeout.Token);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

        // Act
        var result = await client.CallToolAsync("get_weather", arguments, cancellationToken: timeout.Token);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("invalid_arguments", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("untrusted", result.StructuredContent.Value.GetRawText());
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallWeatherTool_MissingAndAmbiguousLocations_DoNotRequestForecast(bool ambiguous)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var second = fixtures.LocationNode();
        second["admin1"] = "Другая область";
        second["latitude"] = 42;
        var locations = ambiguous ? "{\"results\":[" + fixtures.LocationNode().ToJsonString() + "," + second.ToJsonString() + "]}" : "{}";
        var handler = Handler(locations, fixtures.Current());
        using var host = new WeatherProtocolHost(handler);
        await using var server = new InMemoryMcpServer(host.Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await McpClient.CreateAsync(server.ClientTransport, cancellationToken: timeout.Token);

        // Act
        var result = await client.CallToolAsync("get_weather", new Dictionary<string, object?> { ["city"] = "Тбилиси" }, cancellationToken: timeout.Token);

        // Assert
        Assert.False(result.IsError);
        var value = result.StructuredContent!.Value;
        Assert.Equal(ambiguous ? "ambiguous" : "not_found", value.GetProperty("status").GetString());
        Assert.False(value.TryGetProperty("temperature_c", out _));
        Assert.Single(handler.Requests);
        if (ambiguous)
        {
            Assert.Equal(2, value.GetProperty("candidates").GetArrayLength());
            Assert.Equal("GE", value.GetProperty("candidates")[0].GetProperty("country_code").GetString());
            Assert.Equal("Другая область", value.GetProperty("candidates")[1].GetProperty("admin1").GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallWeatherTool_InvalidExternalData_IsStructuredFailure(bool forecast)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var handler = Handler(forecast ? fixtures.Locations() : "{\"results\":null}", "{\"private\":\"secret-body\"}");
        using var host = new WeatherProtocolHost(handler);
        await using var server = new InMemoryMcpServer(host.Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await McpClient.CreateAsync(server.ClientTransport, cancellationToken: timeout.Token);

        // Act
        var result = await client.CallToolAsync("get_weather", new Dictionary<string, object?> { ["city"] = "Тбилиси" }, cancellationToken: timeout.Token);

        // Assert
        Assert.True(result.IsError);
        var value = result.StructuredContent!.Value;
        Assert.Equal("invalid_external_response", value.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("secret-body", value.GetRawText());
        Assert.Equal(forecast ? 2 : 1, handler.Requests.Count);
    }

    [Fact]
    public async Task CallWeatherTool_StdioServer_AdvertisesWeatherAndRejectsInjectedArguments()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var transport = new ItmoBot.Infrastructure.Mcp.StdioMcpTransportFactory().Create(new McpTestSettings().Create());
        await using var client = await McpClient.CreateAsync(transport, loggerFactory: NullLoggerFactory.Instance, cancellationToken: timeout.Token);

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("get_weather", new Dictionary<string, object?> { ["city"] = "Тбилиси", ["url"] = "https://untrusted.invalid" }, cancellationToken: timeout.Token);

        // Assert
        Assert.Contains(tools, item => item.ProtocolTool.Name == "get_weather");
        Assert.True(result.IsError);
        Assert.Equal("invalid_arguments", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    private WeatherHttpHandler Handler(string locations, string current)
    {
        return new WeatherHttpHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.Host.StartsWith("geocoding", StringComparison.Ordinal) ? locations : current),
        }));
    }
}
