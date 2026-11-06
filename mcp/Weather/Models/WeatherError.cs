namespace ItmoBot.Mcp.Weather.Models;

public sealed record class WeatherError(WeatherErrorCode Code, string Message, bool Retryable);
