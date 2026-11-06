using System.Collections;
using ItmoBot.Configuration.Contracts;
using ItmoBot.Configuration.Models;
using ItmoBot.Configuration.Secrets;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Configuration.Loading;

public sealed class SettingsLoader(IEnvironmentFileReader environmentFileReader)
{
    public Result<Settings> Load(string envFile = ".env", IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            return LoadCore(envFile, environment);
        }
        catch (InputValidationException error)
        {
            return new Result<Settings>.Failure(new AppError(error.Code, error.Message));
        }
    }

    private Result<Settings> LoadCore(string envFile, IReadOnlyDictionary<string, string>? environment)
    {
        Result<IReadOnlyDictionary<string, string>> read = environmentFileReader.Read(envFile);

        if (read is Result<IReadOnlyDictionary<string, string>>.Failure readFailure)
        {
            return new Result<Settings>.Failure(readFailure.Error);
        }

        var values = MergeEnvironmentValues(((Result<IReadOnlyDictionary<string, string>>.Success)read).Value, environment);
        return CreateSettings(values);
    }

    private Dictionary<string, string> MergeEnvironmentValues(
        IReadOnlyDictionary<string, string> fileValues,
        IReadOnlyDictionary<string, string>? environment)
    {
        var values = new Dictionary<string, string>(fileValues);

        if (environment is null)
        {
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                values[(string)entry.Key] = (string)entry.Value!;
            }
        }
        else
        {
            foreach (var entry in environment)
            {
                values[entry.Key] = entry.Value;
            }
        }

        return values;
    }

    private Result<Settings> CreateSettings(IReadOnlyDictionary<string, string> values)
    {
        var token = new TelegramBotToken(GetValue(values, "BOT_TOKEN"));
        var password = new SecretValue(GetValue(values, "POSTGRES_PASSWORD"));
        var proxy = ParseProxy(GetValue(values, "TELEGRAM_PROXY_URL"));
        int postgresPort = ParsePort(GetValue(values, "POSTGRES_PORT", "5432"), "POSTGRES_PORT");
        int healthPort = ParsePort(GetValue(values, "HEALTH_PORT", "8080"), "HEALTH_PORT");
        var level = ParseLogLevel(GetValue(values, "LOG_LEVEL", "INFO"));

        if (level == LogLevel.None)
        {
            return new Result<Settings>.Failure(new AppError(
                ErrorCode.InvalidConfiguration, "LOG_LEVEL: используйте DEBUG, INFO, WARNING, ERROR или CRITICAL."));
        }

        return new Result<Settings>.Success(new Settings(
            token,
            password,
            proxy,
            ConfigurationChecks.PostgresHost(GetDatabaseValue(values, "POSTGRES_HOST", "127.0.0.1")),
            postgresPort,
            ConfigurationChecks.DatabaseName(GetDatabaseValue(values, "POSTGRES_DB", "bot")),
            ConfigurationChecks.DatabaseUser(GetDatabaseValue(values, "POSTGRES_USER", "bot")),
            level,
            healthPort,
            CreateLlmSettings(values),
            CreateMcpSettings(values)));
    }

    private McpSettings CreateMcpSettings(IReadOnlyDictionary<string, string> values)
    {
        var path = values.TryGetValue("MCP_SERVER_PATH", out var configured)
            ? configured
            : Path.Combine("mcp", "bin", "Debug", "net10.0", "ItmoBot.Mcp.dll");
        string serverPath = ConfigurationChecks.McpServerPath(path);
        var sourcePath = values.TryGetValue("SCHEDULE_FILE", out var source)
            ? source
            : Path.Combine(Path.GetDirectoryName(serverPath)!, "Data", "schedule.json");
        var timezone = values.TryGetValue("SCHEDULE_TIMEZONE", out var zone) ? zone : "Europe/Moscow";
        return new McpSettings(serverPath,
            ConfigurationChecks.McpTimeout(ParsePositiveInteger(values, "MCP_TIMEOUT_SECONDS", "15")),
            new ScheduleSettings(ConfigurationChecks.ScheduleSourcePath(sourcePath), ConfigurationChecks.IanaTimeZone(timezone)),
            new WeatherHttpSettings(ConfigurationChecks.WeatherHttpTimeout(ParsePositiveInteger(values, "WEATHER_HTTP_TIMEOUT_SECONDS", "5")),
                ConfigurationChecks.WeatherHttpAttempts(ParsePositiveInteger(values, "WEATHER_HTTP_MAX_ATTEMPTS", "2"))));
    }

    private LlmSettings CreateLlmSettings(IReadOnlyDictionary<string, string> values)
    {
        return new LlmSettings(
            ConfigurationChecks.LlmApiAddress(GetValue(values, "LLM_API_BASE_URL")),
            new LlmApiKey(GetValue(values, "OPENROUTER_API_KEY")),
            RequestChecks.ModelName(GetValue(values, "LLM_MODEL")),
            ConfigurationChecks.LlmRequestTimeout(ParsePositiveInteger(values, "LLM_TIMEOUT_SECONDS", "60")),
            RequestChecks.TokenLimit(ParsePositiveInteger(values, "LLM_MAX_OUTPUT_TOKENS", "2048")),
            RequestChecks.TokenLimit(ParsePositiveInteger(values, "HISTORY_MAX_TOKENS", "32768")),
            RequestChecks.HistoryMessageLimit(ParsePositiveInteger(values, "HISTORY_MAX_MESSAGES", "24")),
            RequestChecks.LlmRetryWindow(ParsePositiveInteger(values, "LLM_RETRY_WINDOW_SECONDS", "300")),
            ParseStructuredOutputMode(GetValue(values, "LLM_STRUCTURED_OUTPUT_MODE", "json_schema")));
    }

    private LlmStructuredOutputMode ParseStructuredOutputMode(string value)
    {
        return value switch
        {
            "json_schema" => LlmStructuredOutputMode.JsonSchema,
            "json_object" => LlmStructuredOutputMode.JsonObject,
            "tool_compatible_json" => LlmStructuredOutputMode.ToolCompatibleJson,
            _ => throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "LLM_STRUCTURED_OUTPUT_MODE: используйте json_schema, json_object или tool_compatible_json."),
        };
    }

    private int ParsePositiveInteger(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        var text = values.TryGetValue(key, out var configured) ? configured : fallback;
        if (!int.TryParse(text, out var value) || value <= 0)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                $"{key}: укажите положительное целое число.");
        }

        return value;
    }

    private string GetValue(IReadOnlyDictionary<string, string> values, string key, string fallback = "")
    {
        return values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;
    }

    private ProxyAddress? ParseProxy(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        return new ProxyAddress(text);
    }

    private LogLevel ParseLogLevel(string text)
    {
        return text.ToUpperInvariant() switch
        {
            "DEBUG" => LogLevel.Debug,
            "INFO" => LogLevel.Information,
            "WARNING" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "CRITICAL" => LogLevel.Critical,
            _ => LogLevel.None,
        };
    }

    private string GetDatabaseValue(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        // An explicitly empty database parameter is invalid; only a missing one uses the default.
        return values.TryGetValue(key, out var value) ? value : fallback;
    }

    private int ParsePort(string text, string key)
    {
        if (!int.TryParse(text, out var value) || value is < 1 or > 65535)
        {
            throw new InputValidationException(ErrorCode.InvalidPort,
                $"{key}: нужен номер порта от 1 до 65535.");
        }

        return ConfigurationChecks.PortNumber(value);
    }
}
