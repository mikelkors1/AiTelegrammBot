using System.Net;
using System.Text.Json.Nodes;
using System.Text;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace ItmoBot.Tests.Unit.Weather;

public sealed class WeatherServiceTests
{
    [Fact]
    public async Task GetCurrentWeatherByCityAsync_UniqueCity_ReturnsValidatedWeatherAndExplicitObservationOffset()
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var handler = new WeatherHttpHandler((request, _) => Task.FromResult(Response(
            request.RequestUri!.Host.StartsWith("geocoding-", StringComparison.Ordinal) ? fixtures.Locations() : fixtures.Current())));
        using var client = Client(handler);
        var service = fixtures.Service(client);

        // Act
        var result = await service.GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Тбилиси"), CancellationToken.None);

        // Assert
        var weather = Assert.IsType<WeatherResult.Success>(result).Weather;
        Assert.Equal("Тбилиси", weather.Location.CityName);
        Assert.Equal("Грузия", weather.Location.CountryName);
        Assert.Equal("GE", weather.Location.CountryCode);
        Assert.Equal(18.5, weather.Temperature);
        Assert.Equal(3, weather.ConditionCode);
        Assert.Equal(7.2, weather.WindSpeed);
        Assert.Equal("Asia/Tbilisi", weather.TimeZone.Value.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(4)), weather.ObservedAt);
        Assert.Equal(2, handler.Requests.Count);
        var query = QueryHelpers.ParseQuery(handler.Requests[1].RequestUri!.Query);
        Assert.Equal("41.7151", query["latitude"].ToString());
        Assert.Equal("44.8271", query["longitude"].ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":[]}")]
    public async Task GetCurrentWeatherByCityAsync_UnknownCity_DoesNotRequestForecast(string response)
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response(response)));
        using var client = Client(handler);
        var service = new WeatherFixtures().Service(client);

        // Act
        var result = await service.GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Несуществующий город"), CancellationToken.None);

        // Assert
        Assert.IsType<WeatherResult.NotFound>(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetCurrentWeatherByCityAsync_AmbiguousCity_ReturnsDistinctAreasWithoutRequestingWeather()
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var first = fixtures.LocationNode();
        first["name"] = "Одинаковый город";
        first["admin1"] = "Область A";
        var second = first.DeepClone();
        second["admin1"] = "Область B";
        second["latitude"] = 42.0;
        var response = new JsonObject { ["results"] = new JsonArray(first, second) }.ToJsonString();
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response(response)));
        using var client = Client(handler);

        // Act
        var result = await fixtures.Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Одинаковый город"), CancellationToken.None);

        // Assert
        var candidates = Assert.IsType<WeatherResult.Ambiguous>(result).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Equal(new[] { "Область A", "Область B" }, candidates.Select(location => location.AdministrativeArea!));
        Assert.All(candidates, location => Assert.Equal("GE", location.CountryCode));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("Тбилиси, Грузия")]
    [InlineData("Тбилиси, ge")]
    public async Task GetCurrentWeatherByCityAsync_Country_IsNormalizedAndAppliedBeforeAndAfterGeocoding(string queryText)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var correct = fixtures.LocationNode();
        var otherCountry = correct.DeepClone();
        otherCountry["country_code"] = "US";
        otherCountry["country"] = "США";
        otherCountry["latitude"] = 33.0;
        var locations = new JsonObject { ["results"] = new JsonArray(otherCountry, correct) }.ToJsonString();
        var handler = new WeatherHttpHandler((request, _) => Task.FromResult(Response(
            request.RequestUri!.Host.StartsWith("geocoding-", StringComparison.Ordinal) ? locations : fixtures.Current())));
        using var client = Client(handler);

        // Act
        var result = await fixtures.Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery(queryText), CancellationToken.None);

        // Assert
        Assert.Equal("GE", Assert.IsType<WeatherResult.Success>(result).Weather.Location.CountryCode);
        var query = QueryHelpers.ParseQuery(handler.Requests[0].RequestUri!.Query);
        Assert.Equal("GE", query["countryCode"].ToString());
        Assert.Equal("Тбилиси", query["name"].ToString());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetCurrentWeatherByCityAsync_RegionalQualifier_FiltersCandidatesWithoutGuessingCountry()
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var first = fixtures.LocationNode();
        first["admin1"] = "Область A";
        var second = first.DeepClone();
        second["admin1"] = "Область B";
        second["latitude"] = 42.0;
        var locations = new JsonObject { ["results"] = new JsonArray(first, second) }.ToJsonString();
        var handler = new WeatherHttpHandler((request, _) => Task.FromResult(Response(
            request.RequestUri!.Host.StartsWith("geocoding-", StringComparison.Ordinal) ? locations : fixtures.Current())));
        using var client = Client(handler);

        // Act
        var result = await fixtures.Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Тбилиси, область B"), CancellationToken.None);

        // Assert
        Assert.Equal("Область B", Assert.IsType<WeatherResult.Success>(result).Weather.Location.AdministrativeArea!);
        var query = QueryHelpers.ParseQuery(handler.Requests[0].RequestUri!.Query);
        Assert.False(query.ContainsKey("countryCode"));
        Assert.Equal("Тбилиси, область B", query["name"].ToString());
    }

    [Theory]
    [InlineData("Тбилиси, Georgia")]
    [InlineData("Город, Конго")]
    [InlineData("Город, ZZ")]
    [InlineData("Город, ")]
    [InlineData("Город, страна, область")]
    public async Task GetCurrentWeatherByCityAsync_AmbiguousOrInvalidQualifier_IsRejectedWithoutHttp(string query)
    {
        // Arrange
        var handler = new WeatherHttpHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var client = Client(handler);

        // Act
        var result = await new WeatherFixtures().Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery(query), CancellationToken.None);

        // Assert
        Assert.Equal(WeatherErrorCode.InvalidArguments, Assert.IsType<WeatherResult.Failure>(result).Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetCurrentWeatherByCityAsync_UnsupportedQualifier_DoesNotSilentlyPickReturnedCity()
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var handler = new WeatherHttpHandler((_, _) => Task.FromResult(Response(fixtures.Locations())));
        using var client = Client(handler);

        // Act
        var result = await fixtures.Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Тбилиси, Атлантида"), CancellationToken.None);

        // Assert
        Assert.Equal(WeatherErrorCode.InvalidArguments, Assert.IsType<WeatherResult.Failure>(result).Error.Code);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCurrentWeatherByCityAsync_HttpFailures_ArePreservedAtEitherStage(bool failSearch)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var handler = new WeatherHttpHandler((request, _) =>
        {
            var search = request.RequestUri!.Host.StartsWith("geocoding-", StringComparison.Ordinal);
            return Task.FromResult(search && !failSearch ? Response(fixtures.Locations()) : Response("provider-secret", HttpStatusCode.BadRequest));
        });
        using var client = Client(handler);

        // Act
        var result = await fixtures.Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Тбилиси"), CancellationToken.None);

        // Assert
        var error = Assert.IsType<WeatherResult.Failure>(result).Error;
        Assert.Equal(WeatherErrorCode.SourceUnavailable, error.Code);
        Assert.DoesNotContain("provider-secret", error.Message);
        Assert.Equal(failSearch ? 1 : 2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetCurrentWeatherByCityAsync_OverallDeadline_IsSharedByGeocodingAndWeatherRequests()
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var clock = new ManualTimeProvider();
        var handler = new WeatherHttpHandler(async (request, token) =>
        {
            if (request.RequestUri!.Host.StartsWith("geocoding-", StringComparison.Ordinal))
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                return Response(fixtures.Locations());
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(fixtures.Current());
        });
        using var client = Client(handler, clock);
        var running = fixtures.Service(client, clock, deadline: 3).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Тбилиси"), CancellationToken.None);

        // Act
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(WeatherErrorCode.ExternalTimeout, Assert.IsType<WeatherResult.Failure>(result).Error.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetCurrentWeatherByCityAsync_CallerCancellation_IsNotConvertedIntoWeatherFailure()
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
        var running = new WeatherFixtures().Service(client).GetCurrentWeatherByCityAsync(WeatherChecks.CityQuery("Тбилиси"), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(handler.Requests);
    }

    private OpenMeteoClient Client(WeatherHttpHandler handler, TimeProvider? clock = null)
    {
        return new OpenMeteoClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, new OpenMeteoRequestFactory(),
            new WeatherHttpSettings(WeatherChecks.WeatherHttpTimeout(5), WeatherChecks.WeatherHttpAttempts(2)), clock ?? TimeProvider.System);
    }

    private HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
