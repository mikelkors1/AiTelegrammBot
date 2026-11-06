using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Infrastructure.Weather;

public sealed class OpenMeteoLocationParser(OpenMeteoJsonReader reader)
{
    public WeatherDataResult<IReadOnlyList<WeatherLocation>> ParseLocations(JsonElement response)
    {
        try
        {
            reader.ValidateObject(response);
            if (!response.TryGetProperty("results", out var results))
            {
                return new WeatherDataResult<IReadOnlyList<WeatherLocation>>.Success(Array.Empty<WeatherLocation>());
            }

            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() > 100)
            {
                throw new JsonException("Некорректный список населённых пунктов.");
            }

            var locations = results.EnumerateArray().Select(ParseLocation).ToArray();
            return new WeatherDataResult<IReadOnlyList<WeatherLocation>>.Success(Array.AsReadOnly(locations));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return new WeatherDataResult<IReadOnlyList<WeatherLocation>>.Failure(new WeatherError(
                WeatherErrorCode.InvalidExternalResponse, "Сервис поиска городов вернул некорректные данные.", Retryable: false));
        }
    }

    private WeatherLocation ParseLocation(JsonElement location)
    {
        reader.ValidateObject(location);
        var area = reader.ReadOptionalString(location, "admin1");
        return new WeatherLocation(WeatherChecks.GeographicName(reader.ReadString(location, "name")),
            WeatherChecks.GeographicName(reader.ReadString(location, "country")), WeatherChecks.CountryCode(reader.ReadString(location, "country_code")),
            area is null ? null : WeatherChecks.GeographicName(area),
            new GeoCoordinates(reader.ReadFiniteNumber(location, "latitude"), reader.ReadFiniteNumber(location, "longitude")),
            new UserTimeZone(reader.ReadString(location, "timezone")));
    }
}
