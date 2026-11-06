using System.Text.Json;
using ItmoBot.Mcp.Weather.Models;

namespace ItmoBot.Mcp.Weather.ResultTypes;

public abstract record class WeatherHttpResult
{
    private WeatherHttpResult()
    {
    }

    public sealed record class Success(JsonElement Value) : WeatherHttpResult;

    public sealed record class Failure(WeatherError Error) : WeatherHttpResult;
}
