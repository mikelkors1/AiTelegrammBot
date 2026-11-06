using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Weather;

public sealed class OpenMeteoClientTests
{
    [Fact]
    public async Task SendAsync_AlreadyCancelledRequest_DoesNotSendHttp()
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var running = client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_SuccessfulJson_RemainsAvailableAfterResponseDisposal()
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response("""{"current":{"temperature_2m":12.5}}""")));
        using var client = Client(handler);

        // Act
        var result = await client.GetCurrentWeatherByCoordinatesAsync(new GeoCoordinates(59.93, 30.31), CancellationToken.None);

        // Assert
        var value = Assert.IsType<WeatherHttpResult.Success>(result).Value;
        Assert.Equal(12.5, value.GetProperty("current").GetProperty("temperature_2m").GetDouble());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task SendAsync_TransientHttpFailure_RetriesOnceWithFreshRequest(int status)
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var count = 0;
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(++count == 1
            ? Response("provider-secret", (HttpStatusCode)status)
            : Response("{\"results\":[]}")));
        using var client = Client(handler, clock);
        var running = client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));

        // Act
        clock.Advance(TimeSpan.FromMilliseconds(250));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<WeatherHttpResult.Success>(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotSame(handler.Requests[0], handler.Requests[1]);
        Assert.Equal(handler.Requests[0].RequestUri, handler.Requests[1].RequestUri);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public async Task SendAsync_OtherHttpFailures_AreNotRetriedAndDoNotExposeBody(int status)
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response("provider-secret; private-url", (HttpStatusCode)status)));
        using var client = Client(handler);

        // Act
        var result = await client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);

        // Assert
        var error = Assert.IsType<WeatherHttpResult.Failure>(result).Error;
        Assert.Equal(WeatherErrorCode.SourceUnavailable, error.Code);
        Assert.False(error.Retryable);
        Assert.DoesNotContain("provider-secret", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_IsRetriedButNeverExceedsConfiguredAttempts()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var handler = new WeatherHttpHandler((_, _) => throw new HttpRequestException("private-address; provider-secret"));
        using var client = Client(handler, clock);
        var running = client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));

        // Act
        clock.Advance(TimeSpan.FromMilliseconds(250));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        var error = Assert.IsType<WeatherHttpResult.Failure>(result).Error;
        Assert.Equal(WeatherErrorCode.SourceUnavailable, error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("private-address", error.Message);
    }

    [Fact]
    public async Task SendAsync_OneAttemptConfiguration_DisablesRetry()
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response("", HttpStatusCode.ServiceUnavailable)));
        using var client = Client(handler, attempts: 1);

        // Act
        var result = await client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);

        // Assert
        Assert.IsType<WeatherHttpResult.Failure>(result);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"error\":true,\"reason\":\"provider-secret\"}")]
    public async Task SendAsync_InvalidJsonEnvelope_IsRejectedWithoutRetry(string body)
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response(body)));
        using var client = Client(handler);

        // Act
        var result = await client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);

        // Assert
        var error = Assert.IsType<WeatherHttpResult.Failure>(result).Error;
        Assert.Equal(WeatherErrorCode.InvalidExternalResponse, error.Code);
        Assert.False(error.Retryable);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("provider-secret", error.Message);
    }

    [Fact]
    public async Task SendAsync_Timeout_CoversBodyReadingAfterHeadersAndDoesNotRetry()
    {
        // Arrange
        var pipe = new Pipe();
        var clock = new ManualTimeProvider();
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(pipe.Reader.AsStream()),
        }));
        using var client = Client(handler, clock);
        var running = client.GetCurrentWeatherByCoordinatesAsync(new GeoCoordinates(59.93, 30.31), CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));

        // Act
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        await pipe.Writer.CompleteAsync();

        // Assert
        Assert.Equal(WeatherErrorCode.ExternalTimeout, Assert.IsType<WeatherHttpResult.Failure>(result).Error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_CancellationDuringHttp_PropagatesInsteadOfReturningTimeout()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new WeatherHttpHandler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response("{}");
        });
        using var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        var running = client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_CancellationDuringRetryWait_PreventsSecondRequest()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response("", HttpStatusCode.ServiceUnavailable)));
        using var client = Client(handler, clock);
        using var cancellation = new CancellationTokenSource();
        var running = client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), cancellation.Token);
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_ShortRetryAfter_IsRespected(bool useDate)
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var count = 0;
        var handler = new WeatherHttpHandler((_, _) =>
        {
            var response = Response("{}", ++count == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (count == 1)
            {
                response.Headers.RetryAfter = useDate
                    ? new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(1))
                    : new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            }

            return Task.FromResult(response);
        });
        using var client = Client(handler, clock);
        var running = client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));

        // Act
        clock.Advance(TimeSpan.FromMilliseconds(999));

        // Assert
        Assert.Single(handler.Requests);
        Assert.False(running.IsCompleted);

        // Act
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<WeatherHttpResult.Success>(result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_LongRetryAfter_ReturnsUnavailableWithoutEarlyRetry()
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) =>
        {
            var response = Response("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        });
        using var client = Client(handler);

        // Act
        var result = await client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);

        // Assert
        Assert.Equal(WeatherErrorCode.SourceUnavailable, Assert.IsType<WeatherHttpResult.Failure>(result).Error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_ExcessiveBody_IsRejectedAndClientOwnsHandlerDisposal()
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response(new string('x', 1_048_577))));
        using var client = Client(handler);

        // Act
        var result = await client.SearchLocationsByNameAsync(WeatherChecks.CityQuery("Казань"), CancellationToken.None);
        client.Dispose();
        client.Dispose();

        // Assert
        Assert.Equal(WeatherErrorCode.InvalidExternalResponse, Assert.IsType<WeatherHttpResult.Failure>(result).Error.Code);
        Assert.Equal(1, handler.DisposeCalls);
    }

    private OpenMeteoClient Client(WeatherHttpHandler handler, TimeProvider? clock = null, int attempts = 2)
    {
        return new OpenMeteoClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, new OpenMeteoRequestFactory(),
            new WeatherHttpSettings(WeatherChecks.WeatherHttpTimeout(5), WeatherChecks.WeatherHttpAttempts(attempts)), clock ?? TimeProvider.System);
    }

    private HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
