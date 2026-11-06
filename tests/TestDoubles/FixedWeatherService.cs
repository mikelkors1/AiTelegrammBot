using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FixedWeatherService(WeatherResult result) : IWeatherService
{
    public int Calls
    {
        get; private set;
    }

    public Task<WeatherResult> GetCurrentWeatherByCityAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(result);
    }
}
