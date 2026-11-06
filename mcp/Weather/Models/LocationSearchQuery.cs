namespace ItmoBot.Mcp.Weather.Models;

public sealed record class LocationSearchQuery(string SearchText, string? CountryFilter, string? Qualifier);
