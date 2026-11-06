using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Infrastructure.Weather;

public sealed class OpenMeteoWeatherParser(OpenMeteoJsonReader reader)
{
    public WeatherDataResult<CurrentWeather> ParseCurrentWeather(JsonElement response, WeatherLocation location)
    {
        try
        {
            reader.ValidateObject(response);
            var timezone = new UserTimeZone(reader.ReadString(response, "timezone"));
            var current = reader.ReadProperty(response, "current");
            var units = reader.ReadProperty(response, "current_units");
            reader.ValidateObject(current);
            reader.ValidateObject(units);
            ValidateUnits(units);
            var result = new CurrentWeather(location,
                WeatherChecks.TemperatureCelsius(reader.ReadFiniteNumber(current, "temperature_2m")),
                WeatherChecks.WeatherConditionCode(checked((int)reader.ReadInteger(current, "weather_code"))),
                WeatherChecks.WindSpeedKmh(reader.ReadFiniteNumber(current, "wind_speed_10m")),
                WeatherChecks.WeatherObservationTime(reader.ReadInteger(current, "time"), timezone), timezone);
            return new WeatherDataResult<CurrentWeather>.Success(result);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return new WeatherDataResult<CurrentWeather>.Failure(new WeatherError(WeatherErrorCode.InvalidExternalResponse,
                "Погодный сервис вернул некорректные наблюдения или единицы измерения.", Retryable: false));
        }
    }

    private void ValidateUnits(JsonElement units)
    {
        if (reader.ReadString(units, "temperature_2m") != "°C" || reader.ReadString(units, "wind_speed_10m") != "km/h"
            || reader.ReadString(units, "time") != "unixtime"
            || !reader.ReadString(units, "weather_code").Equals("wmo code", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("Несоответствие единиц измерения запрошенному формату.");
        }
    }
}
