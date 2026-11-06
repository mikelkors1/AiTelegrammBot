using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using NodaTime;

namespace ItmoBot.Validation;

public static class ConfigurationChecks
{
    public static string DatabaseUser(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "POSTGRES_USER: укажите непустое значение без управляющих символов.");
        }

        return value;
    }

    public static string ScheduleSourcePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "SCHEDULE_FILE: укажите путь к JSON-файлу расписания.");
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "SCHEDULE_FILE: указан некорректный путь.");
        }
    }

    public static int WeatherHttpAttempts(int value)
    {
        if (value is < 1 or > 2)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "WEATHER_HTTP_MAX_ATTEMPTS: укажите целое число от 1 до 2.");
        }

        return value;
    }

    public static TimeSpan McpTimeout(int seconds)
    {
        if (seconds is < 1 or > 15)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "MCP_TIMEOUT_SECONDS: укажите целое число от 1 до 15.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static TimeSpan WeatherHttpTimeout(int seconds)
    {
        if (seconds is < 1 or > 5)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "WEATHER_HTTP_TIMEOUT_SECONDS: укажите целое число от 1 до 5.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static TimeSpan LlmRequestTimeout(int seconds)
    {
        if (seconds is < 1 or > 300)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "LLM_TIMEOUT_SECONDS: укажите целое число от 1 до 300.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static Uri LlmApiAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new InputValidationException(ErrorCode.InvalidLlmApiAddress,
                "LLM_API_BASE_URL: укажите HTTPS-адрес API без учётных данных, query и fragment; HTTP допускается только для локального сервиса.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + '/');
    }

    public static string FormatLlmApiAddress(Uri value)
    {
        return value.AbsoluteUri;
    }

    public static string McpServerPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)
            || !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "MCP_SERVER_PATH: укажите путь к сборке MCP-сервера с расширением .dll.");
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "MCP_SERVER_PATH: указан некорректный путь к сборке MCP-сервера.");
        }
    }

    public static string DatabaseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "POSTGRES_DB: укажите непустое значение без управляющих символов.");
        }

        return value;
    }

    public static string PostgresHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "POSTGRES_HOST: укажите непустое значение без управляющих символов.");
        }

        return value;
    }

    public static int PortNumber(int value)
    {
        if (value is < 1 or > 65535)
        {
            throw new InputValidationException(ErrorCode.InvalidPort, "Номер порта должен быть от 1 до 65535.");
        }

        return value;
    }

    public static string IanaTimeZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || DateTimeZoneProviders.Tzdb.GetZoneOrNull(value) is null)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "SCHEDULE_TIMEZONE: укажите существующую IANA-зону, например Europe/Moscow.");
        }

        return value;
    }

}
