using ItmoBot.Mcp.Weather.Models;

namespace ItmoBot.Mcp.Weather.ResultTypes;

public abstract record class WeatherResult
{
    private WeatherResult()
    {
    }

    public sealed record class Success(CurrentWeather Weather) : WeatherResult;

    public sealed record class NotFound(string Query) : WeatherResult;

    public sealed record class Ambiguous(IReadOnlyList<WeatherLocation> Candidates) : WeatherResult;

    public sealed record class Failure(WeatherError Error) : WeatherResult;
}
