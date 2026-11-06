using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Weather.Contracts;

public interface IWeatherService
{
    Task<WeatherResult> GetCurrentWeatherByCityAsync(string query, CancellationToken cancellationToken);
}
