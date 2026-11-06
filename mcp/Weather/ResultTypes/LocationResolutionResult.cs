using ItmoBot.Mcp.Weather.Models;

namespace ItmoBot.Mcp.Weather.ResultTypes;

public abstract record class LocationResolutionResult
{
    private LocationResolutionResult()
    {
    }

    public sealed record class Success(WeatherLocation Location) : LocationResolutionResult;

    public sealed record class NotFound : LocationResolutionResult;

    public sealed record class Ambiguous(IReadOnlyList<WeatherLocation> Candidates) : LocationResolutionResult;

    public sealed record class Failure(WeatherError Error) : LocationResolutionResult;
}
