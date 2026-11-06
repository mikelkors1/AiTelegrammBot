using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Weather.Contracts;

public interface IOpenMeteoClient
{
    Task<WeatherHttpResult> SearchLocationsByNameAsync(string query, CancellationToken cancellationToken, string? countryFilter = null);

    Task<WeatherHttpResult> GetCurrentWeatherByCoordinatesAsync(GeoCoordinates coordinates, CancellationToken cancellationToken);
}
