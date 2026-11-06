using System.Text.Json;
using ItmoBot.Mcp.Protocol;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;
using ItmoBot.Tests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ItmoBot.Tests.Unit.Weather;

public sealed class WeatherEndpointTests
{
    [Theory]
    [InlineData(WeatherErrorCode.InvalidArguments, "invalid_arguments", false)]
    [InlineData(WeatherErrorCode.SourceUnavailable, "source_unavailable", true)]
    [InlineData(WeatherErrorCode.ExternalTimeout, "external_timeout", true)]
    [InlineData(WeatherErrorCode.InvalidExternalResponse, "invalid_external_response", false)]
    public async Task CallAsync_ExpectedFailures_PreserveCodeAndRetryability(WeatherErrorCode code, string expected, bool retryable)
    {
        // Arrange
        var service = new FixedWeatherService(new WeatherResult.Failure(new WeatherError(code, "Безопасная ошибка.", retryable)));
        var endpoint = new WeatherMcpEndpoint(service);
        var request = Request("Тбилиси");

        // Act
        var result = await endpoint.CallAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        var error = result.StructuredContent!.Value.GetProperty("error");
        Assert.Equal(expected, error.GetProperty("code").GetString());
        Assert.Equal(retryable, error.GetProperty("retryable").GetBoolean());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task CallAsync_OversizedCity_DoesNotCallService()
    {
        // Arrange
        var service = new FixedWeatherService(new WeatherResult.Failure(new WeatherError(WeatherErrorCode.SourceUnavailable, "Недоступно.", true)));
        var endpoint = new WeatherMcpEndpoint(service);

        // Act
        var result = await endpoint.CallAsync(Request(new string('a', 101)), CancellationToken.None);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task CallAsync_Cancellation_IsNotConvertedToToolFailure()
    {
        // Arrange
        var service = new FixedWeatherService(new WeatherResult.Failure(new WeatherError(WeatherErrorCode.SourceUnavailable, "Недоступно.", true)));
        var endpoint = new WeatherMcpEndpoint(service);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var running = endpoint.CallAsync(Request("Тбилиси"), cancellation.Token).AsTask();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(0, service.Calls);
    }

    private CallToolRequestParams Request(string city)
    {
        return new CallToolRequestParams
        {
            Name = "get_weather",
            Arguments = new Dictionary<string, JsonElement> { ["city"] = JsonSerializer.SerializeToElement(city) },
        };
    }
}
