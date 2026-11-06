using ItmoBot.Application.Agent.Models;

namespace ItmoBot.Mcp.Weather.Models;

public sealed record class WeatherLocation(string CityName, string CountryName,
    string CountryCode, string? AdministrativeArea, GeoCoordinates Coordinates, UserTimeZone TimeZone);
