using ItmoBot.Application.Agent.Models;

namespace ItmoBot.Mcp.Weather.Models;

public sealed record class CurrentWeather(WeatherLocation Location, double Temperature,
    int ConditionCode, double WindSpeed, DateTimeOffset ObservedAt, UserTimeZone TimeZone);
