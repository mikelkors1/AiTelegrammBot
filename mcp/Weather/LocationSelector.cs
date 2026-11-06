using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Weather;

public sealed class LocationSelector(LocationTextNormalizer normalizer)
{
    public LocationResolutionResult SelectLocation(LocationSearchQuery query, IReadOnlyList<WeatherLocation> candidates)
    {
        var matching = candidates.Where(location => MatchesQualifier(query, location))
            .DistinctBy(location => (normalizer.NormalizeName(location.CityName), location.CountryCode,
                normalizer.NormalizeName(location.AdministrativeArea ?? ""), location.Coordinates, location.TimeZone.Value.Id))
            .ToArray();
        if (query.Qualifier is not null && matching.Length == 0)
        {
            return new LocationResolutionResult.Failure(new WeatherError(WeatherErrorCode.InvalidArguments,
                "Уточнение страны или области не распознано. Укажите ISO-код страны или область из вариантов поиска.", Retryable: false));
        }

        if (matching.Length == 0)
        {
            return new LocationResolutionResult.NotFound();
        }

        if (candidates.Count >= 100 && matching.Length == 1)
        {
            return new LocationResolutionResult.Failure(new WeatherError(WeatherErrorCode.InvalidArguments,
                "Поиск вернул слишком много совпадений. Уточните страну или область.", Retryable: false));
        }

        return matching.Length == 1
            ? new LocationResolutionResult.Success(matching[0])
            : new LocationResolutionResult.Ambiguous(Array.AsReadOnly(matching));
    }

    private bool MatchesQualifier(LocationSearchQuery query, WeatherLocation location)
    {
        if (query.CountryFilter is not null && query.CountryFilter != location.CountryCode)
        {
            return false;
        }

        if (query.Qualifier is null)
        {
            return true;
        }

        var expected = normalizer.NormalizeName(query.Qualifier);
        return normalizer.NormalizeName(location.CountryName) == expected
            || normalizer.NormalizeName(location.AdministrativeArea ?? "") == expected;
    }
}
