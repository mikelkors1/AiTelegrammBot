using System.Globalization;
using ItmoBot.Application.Agent.Models;
using NodaTime;

namespace ItmoBot.Mcp.Validation;

public static class WeatherChecks
{
    public static int WeatherHttpAttempts(int value)
    {
        if (value is < 1 or > 2)
        {
            throw new ArgumentException("WEATHER_HTTP_MAX_ATTEMPTS: укажите целое число от 1 до 2.");
        }

        return value;
    }

    public static DateTimeOffset WeatherObservationTime(long unixSeconds, UserTimeZone timezone)
    {
        ArgumentNullException.ThrowIfNull(timezone);
        return Instant.FromUnixTimeSeconds(unixSeconds).InZone(timezone.Value).ToDateTimeOffset();
    }

    public static double TemperatureCelsius(double value)
    {
        if (!double.IsFinite(value) || value < -273.15)
        {
            throw new ArgumentException("Температура должна быть конечным числом не ниже абсолютного нуля.");
        }

        return value;
    }

    public static TimeSpan WeatherHttpTimeout(int seconds)
    {
        if (seconds is < 1 or > 5)
        {
            throw new ArgumentException("WEATHER_HTTP_TIMEOUT_SECONDS: укажите целое число от 1 до 5.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static string GeographicName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Географическое название должно содержать от 1 до 200 символов без управляющих символов.");
        }

        return value.Trim();
    }

    public static int WeatherConditionCode(int value)
    {
        if (value is not (0 or 1 or 2 or 3 or 45 or 48 or 51 or 53 or 55 or 56 or 57
            or 61 or 63 or 65 or 66 or 67 or 71 or 73 or 75 or 77 or 80 or 81 or 82 or 85 or 86 or 95 or 96 or 97 or 99))
        {
            throw new ArgumentException("Неизвестный погодный код Open-Meteo/WMO.");
        }

        return value;
    }

    public static TimeSpan WeatherRequestTimeout(int seconds)
    {
        if (seconds is < 1 or > 15)
        {
            throw new ArgumentException("MCP_TIMEOUT_SECONDS: укажите целое число от 1 до 15.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static double WindSpeedKmh(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentException("Скорость ветра должна быть конечным неотрицательным числом.");
        }

        return value;
    }

    public static string CityQuery(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100 || value.Trim().Any(char.IsControl))
        {
            throw new ArgumentException("Город должен содержать от 1 до 100 символов без управляющих символов.");
        }

        return value.Trim();
    }

    public static string CountryCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 2 || value.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("Страна должна иметь двухбуквенный ISO-код.");
        }

        var normalized = value.ToUpperInvariant();
        try
        {
            if (normalized == "IV" || new RegionInfo(normalized).TwoLetterISORegionName != normalized)
            {
                throw new ArgumentException();
            }
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Указан неподдерживаемый ISO-код страны.");
        }

        return normalized;
    }

}
