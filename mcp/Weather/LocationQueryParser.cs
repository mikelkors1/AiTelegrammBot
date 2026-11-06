using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Weather;

public sealed class LocationQueryParser
{
    private readonly LocationTextNormalizer normalizer;
    private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);

    public LocationQueryParser(LocationTextNormalizer normalizer)
    {
        this.normalizer = normalizer;
        var countries = new Dictionary<string, string>
        {
            ["RU"] = "Россия|РФ|Российская Федерация|Russia|Russian Federation",
            ["GE"] = "Грузия",
            ["US"] = "США|USA|United States|United States of America|Соединённые Штаты|Соединённые Штаты Америки",
            ["GB"] = "Великобритания|UK|United Kingdom|Great Britain",
            ["DE"] = "Германия|Germany|Deutschland",
            ["FR"] = "Франция|France",
            ["KZ"] = "Казахстан|Kazakhstan",
            ["AM"] = "Армения|Armenia",
            ["AZ"] = "Азербайджан|Azerbaijan",
            ["BY"] = "Беларусь|Белоруссия|Belarus",
            ["TR"] = "Турция|Turkey|Türkiye",
            ["IT"] = "Италия|Italy",
            ["ES"] = "Испания|Spain",
            ["FI"] = "Финляндия|Finland",
            ["JP"] = "Япония|Japan",
            ["CH"] = "Швейцария|Switzerland",
            ["CN"] = "Китай|China",
            ["KR"] = "Южная Корея|South Korea",
            ["KP"] = "Северная Корея|North Korea",
        };
        foreach (var country in countries)
        {
            foreach (var alias in country.Value.Split('|'))
            {
                aliases.Add(normalizer.NormalizeName(alias), WeatherChecks.CountryCode(country.Key));
            }
        }
    }

    public WeatherDataResult<LocationSearchQuery> ParseCityQuery(string query)
    {
        try
        {
            var parts = query.Split(',');
            if (parts.Length == 1)
            {
                return new WeatherDataResult<LocationSearchQuery>.Success(new LocationSearchQuery(query, null, null));
            }

            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                return InvalidQualifier();
            }

            string city = WeatherChecks.CityQuery(parts[0]);
            var qualifier = normalizer.NormalizeName(parts[1]);
            if (qualifier is "GEORGIA" or "CONGO" or "КОНГО" or "KOREA" or "КОРЕЯ")
            {
                return InvalidQualifier();
            }

            if (aliases.TryGetValue(qualifier, out var country))
            {
                return new WeatherDataResult<LocationSearchQuery>.Success(new LocationSearchQuery(city, country, null));
            }

            if (qualifier.Length == 2 && qualifier.All(char.IsAsciiLetter))
            {
                return new WeatherDataResult<LocationSearchQuery>.Success(new LocationSearchQuery(city, WeatherChecks.CountryCode(qualifier), null));
            }

            return new WeatherDataResult<LocationSearchQuery>.Success(new LocationSearchQuery(
                WeatherChecks.CityQuery($"{city}, {parts[1].Trim()}"), null, WeatherChecks.GeographicName(parts[1])));
        }
        catch (ArgumentException)
        {
            return InvalidQualifier();
        }
    }

    private WeatherDataResult<LocationSearchQuery>.Failure InvalidQualifier()
    {
        return new WeatherDataResult<LocationSearchQuery>.Failure(new WeatherError(WeatherErrorCode.InvalidArguments,
            "Уточните город и страну двухбуквенным ISO-кодом, например «Тбилиси, GE», либо укажите полное название области.", Retryable: false));
    }
}
