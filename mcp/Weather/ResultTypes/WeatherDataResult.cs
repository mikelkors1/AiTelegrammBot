using ItmoBot.Mcp.Weather.Models;

namespace ItmoBot.Mcp.Weather.ResultTypes;

public abstract record class WeatherDataResult<T>
{
    private WeatherDataResult()
    {
    }

    public sealed record class Success(T Value) : WeatherDataResult<T>;

    public sealed record class Failure(WeatherError Error) : WeatherDataResult<T>;
}
