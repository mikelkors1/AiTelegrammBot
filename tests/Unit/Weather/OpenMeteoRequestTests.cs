using System.Globalization;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Models;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace ItmoBot.Tests.Unit.Weather;

public sealed class OpenMeteoRequestTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Казань\nКазань")]
    public void CreateRequest_InvalidCityQuery_IsRejectedBeforeRequestCreation(string input)
    {
        // Arrange
        var value = input;

        // Act
        var error = Assert.Throws<ArgumentException>(() => WeatherChecks.CityQuery(value));

        // Assert
        Assert.Contains("Город", error.Message);
    }

    [Fact]
    public void CreateRequest_UserQuery_IsEncodedWithoutChangingFixedGeocodingAddress()
    {
        // Arrange
        string query = WeatherChecks.CityQuery(" Казань&count=1&url=https://example.invalid ");

        // Act
        using var request = new OpenMeteoRequestFactory().CreateLocationSearchRequest(query);

        // Assert
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("geocoding-api.open-meteo.com", request.RequestUri.Host);
        Assert.Equal("/v1/search", request.RequestUri.AbsolutePath);
        var values = QueryHelpers.ParseQuery(request.RequestUri.Query);
        Assert.Equal(4, values.Count);
        Assert.Equal(query, values["name"].ToString());
        Assert.Equal("100", values["count"].ToString());
    }

    [Fact]
    public void CreateRequest_Coordinates_UseInvariantFormatAndOnlyCurrentWeatherIsRequested()
    {
        // Arrange
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            // Act
            using var request = new OpenMeteoRequestFactory().CreateCurrentWeatherRequest(new GeoCoordinates(59.93, 30.31));

            // Assert
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.open-meteo.com", request.RequestUri.Host);
            Assert.Equal("/v1/forecast", request.RequestUri.AbsolutePath);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal("59.93", query["latitude"].ToString());
            Assert.Equal("30.31", query["longitude"].ToString());
            Assert.Equal("temperature_2m,weather_code,wind_speed_10m", query["current"].ToString());
            Assert.Equal("auto", query["timezone"].ToString());
            Assert.Equal("unixtime", query["timeformat"].ToString());
            Assert.False(query.ContainsKey("hourly"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void CreateRequest_InvalidCoordinates_AreRejected(double latitude, double longitude)
    {
        // Arrange
        var point = (latitude, longitude);

        // Act
        var error = Assert.Throws<ArgumentException>(() => new GeoCoordinates(point.latitude, point.longitude));

        // Assert
        Assert.Contains("Координаты", error.Message);
    }
}
