using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Infrastructure.Weather;

internal sealed record class WeatherHttpAttempt(WeatherHttpResult Result, TimeSpan? RetryDelay = null);
