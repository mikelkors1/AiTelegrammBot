namespace ItmoBot.Configuration.Models;

public sealed record class WeatherHttpSettings(TimeSpan Timeout, int Attempts);
