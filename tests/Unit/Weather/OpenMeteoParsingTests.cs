using System.Text.Json.Nodes;
using System.Text.Json;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Weather;

public sealed class OpenMeteoParsingTests
{
    [Theory]
    [InlineData("name", "\"\"")]
    [InlineData("country", "null")]
    [InlineData("country_code", "\"ZZ\"")]
    [InlineData("latitude", "91")]
    [InlineData("longitude", "181")]
    [InlineData("latitude", "\"41.7\"")]
    [InlineData("timezone", "\"Mars/Olympus\"")]
    [InlineData("timezone", "null")]
    public void Parse_InvalidCandidate_MakesWholeResponseInvalid(string field, string replacement)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var valid = fixtures.LocationNode();
        var invalid = valid.DeepClone();
        invalid[field] = JsonNode.Parse(replacement);
        var response = fixtures.Json(new JsonObject { ["results"] = new JsonArray(valid, invalid) }.ToJsonString());

        // Act
        var result = new OpenMeteoLocationParser(new OpenMeteoJsonReader()).ParseLocations(response);

        // Assert
        var error = Assert.IsType<WeatherDataResult<IReadOnlyList<WeatherLocation>>.Failure>(result).Error;
        Assert.Equal(WeatherErrorCode.InvalidExternalResponse, error.Code);
        Assert.DoesNotContain("Olympus", error.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"results\":null}")]
    [InlineData("{\"results\":[null]}")]
    [InlineData("{\"results\":[],\"results\":[]}")]
    [InlineData("{\"error\":true,\"reason\":\"secret\"}")]
    public void Parse_InvalidGeocodingEnvelope_IsNotTreatedAsCityNotFound(string json)
    {
        // Arrange
        var response = new WeatherFixtures().Json(json);

        // Act
        var result = new OpenMeteoLocationParser(new OpenMeteoJsonReader()).ParseLocations(response);

        // Assert
        Assert.Equal(WeatherErrorCode.InvalidExternalResponse,
            Assert.IsType<WeatherDataResult<IReadOnlyList<WeatherLocation>>.Failure>(result).Error.Code);
    }

    [Theory]
    [InlineData("current", "temperature_2m", "null")]
    [InlineData("current", "temperature_2m", "\"18.5\"")]
    [InlineData("current", "temperature_2m", "1e400")]
    [InlineData("current", "temperature_2m", "-300")]
    [InlineData("current", "wind_speed_10m", "-1")]
    [InlineData("current", "weather_code", "999")]
    [InlineData("current", "weather_code", "3.5")]
    [InlineData("current", "time", "\"2026-10-12T06:00\"")]
    [InlineData("current", "time", "9223372036854775807")]
    [InlineData("current_units", "temperature_2m", "\"°F\"")]
    [InlineData("current_units", "wind_speed_10m", "\"m/s\"")]
    [InlineData("current_units", "time", "\"iso8601\"")]
    public void Parse_InvalidReadingOrUnits_AreRejected(string section, string field, string replacement)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var response = JsonNode.Parse(fixtures.Current())!;
        response[section]![field] = JsonNode.Parse(replacement);

        // Act
        var result = ParseWeather(fixtures.Json(response.ToJsonString()));

        // Assert
        Assert.Equal(WeatherErrorCode.InvalidExternalResponse, Assert.IsType<WeatherDataResult<CurrentWeather>.Failure>(result).Error.Code);
    }

    [Theory]
    [InlineData("timezone")]
    [InlineData("current")]
    [InlineData("current_units")]
    public void Parse_MissingWeatherSection_IsRejected(string field)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var response = JsonNode.Parse(fixtures.Current())!.AsObject();
        response.Remove(field);

        // Act
        var result = ParseWeather(fixtures.Json(response.ToJsonString()));

        // Assert
        Assert.Equal(WeatherErrorCode.InvalidExternalResponse, Assert.IsType<WeatherDataResult<CurrentWeather>.Failure>(result).Error.Code);
    }

    [Theory]
    [InlineData(1792888200L, 2)]
    [InlineData(1792891800L, 1)]
    public void Parse_UnixObservation_UsesActualIanaOffsetAcrossDst(long timestamp, int expectedOffsetHours)
    {
        // Arrange
        var fixtures = new WeatherFixtures();
        var response = JsonNode.Parse(fixtures.Current())!;
        response["timezone"] = "Europe/Berlin";
        response["current"]!["time"] = timestamp;
        response["current"]!["weather_code"] = 97;

        // Act
        var result = ParseWeather(fixtures.Json(response.ToJsonString()));

        // Assert
        var weather = Assert.IsType<WeatherDataResult<CurrentWeather>.Success>(result).Value;
        Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), weather.ObservedAt.Offset);
        Assert.Equal(new TimeOnly(2, 30), TimeOnly.FromDateTime(weather.ObservedAt.DateTime));
        Assert.Equal(timestamp, weather.ObservedAt.ToUnixTimeSeconds());
        Assert.Equal(97, weather.ConditionCode);
    }

    private WeatherDataResult<CurrentWeather> ParseWeather(JsonElement response)
    {
        var fixtures = new WeatherFixtures();
        var reader = new OpenMeteoJsonReader();
        var location = Assert.IsType<WeatherDataResult<IReadOnlyList<WeatherLocation>>.Success>(
            new OpenMeteoLocationParser(reader).ParseLocations(fixtures.Json(fixtures.Locations()))).Value[0];
        return new OpenMeteoWeatherParser(reader).ParseCurrentWeather(response, location);
    }
}
