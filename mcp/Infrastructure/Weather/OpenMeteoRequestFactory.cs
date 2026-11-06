using System.Globalization;
using ItmoBot.Mcp.Weather.Models;

namespace ItmoBot.Mcp.Infrastructure.Weather;

public sealed class OpenMeteoRequestFactory
{
    public HttpRequestMessage CreateLocationSearchRequest(string query, string? countryFilter = null)
    {
        var filter = countryFilter is null ? "" : $"&countryCode={countryFilter}";
        return new HttpRequestMessage(HttpMethod.Get,
            $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=100&language=ru&format=json{filter}");
    }

    public HttpRequestMessage CreateCurrentWeatherRequest(GeoCoordinates coordinates)
    {
        var latitude = coordinates.Latitude.ToString("R", CultureInfo.InvariantCulture);
        var longitude = coordinates.Longitude.ToString("R", CultureInfo.InvariantCulture);
        return new HttpRequestMessage(HttpMethod.Get,
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}"
            + "&current=temperature_2m,weather_code,wind_speed_10m&temperature_unit=celsius&wind_speed_unit=kmh&timezone=auto&timeformat=unixtime&forecast_days=1");
    }
}
