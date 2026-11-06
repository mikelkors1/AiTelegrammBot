namespace ItmoBot.Mcp.Weather.Models;

public sealed record class WeatherHttpSettings(TimeSpan Timeout, int Attempts);
