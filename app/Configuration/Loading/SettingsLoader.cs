using System.Collections;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Configuration.Models;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Configuration.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Configuration.Contracts;
using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Configuration.Loading;

public sealed class SettingsLoader(IEnvironmentFileReader environmentFileReader) : ISettingsLoader
{
    public SettingsLoadResult Load(string envFile = ".env", IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            return LoadCore(envFile, environment);
        }
        catch (ValueObjectValidationException error)
        {
            return new SettingsLoadResult.Failure(new AppError(error.Code, error.Message));
        }
    }

    private SettingsLoadResult LoadCore(string envFile, IReadOnlyDictionary<string, string>? environment)
    {
        var read = environmentFileReader.Read(envFile);

        if (read is EnvironmentReadResult.Failure readFailure)
        {
            return new SettingsLoadResult.Failure(readFailure.Error);
        }

        var values = MergeEnvironmentValues(((EnvironmentReadResult.Success)read).Values, environment);
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

    private SettingsLoadResult CreateSettings(IReadOnlyDictionary<string, string> values)
    {
        var token = new TelegramBotToken(GetValue(values, "BOT_TOKEN"));
        var password = new SecretValue(GetValue(values, "POSTGRES_PASSWORD"));
        var proxy = ParseProxy(GetValue(values, "TELEGRAM_PROXY_URL"));
        var postgresPort = ParsePort(GetValue(values, "POSTGRES_PORT", "5432"), "POSTGRES_PORT");
        var healthPort = ParsePort(GetValue(values, "HEALTH_PORT", "8080"), "HEALTH_PORT");
        var level = ParseLogLevel(GetValue(values, "LOG_LEVEL", "INFO"));

        if (level == LogLevel.None)
        {
            return new SettingsLoadResult.Failure(new AppError(
                ErrorCode.InvalidConfiguration, "LOG_LEVEL: используйте DEBUG, INFO, WARNING, ERROR или CRITICAL."));
        }

        return new SettingsLoadResult.Success(new Settings(
            token,
            password,
            proxy,
            new PostgresHost(GetDatabaseValue(values, "POSTGRES_HOST", "127.0.0.1")),
            postgresPort,
            new DatabaseName(GetDatabaseValue(values, "POSTGRES_DB", "bot")),
            new DatabaseUser(GetDatabaseValue(values, "POSTGRES_USER", "bot")),
            level,
            healthPort,
            CreateLlmSettings(values)));
    }

    private LlmSettings CreateLlmSettings(IReadOnlyDictionary<string, string> values)
    {
        return new LlmSettings(
            new LlmApiAddress(GetValue(values, "LLM_API_BASE_URL")),
            new LlmApiKey(GetValue(values, "OPENROUTER_API_KEY")),
            new ModelName(GetValue(values, "LLM_MODEL")),
            new LlmRequestTimeout(ParsePositiveInteger(values, "LLM_TIMEOUT_SECONDS", "60")),
            new TokenLimit(ParsePositiveInteger(values, "LLM_MAX_OUTPUT_TOKENS", "2048")),
            new TokenLimit(ParsePositiveInteger(values, "HISTORY_MAX_TOKENS", "8192")),
            new HistoryMessageLimit(ParsePositiveInteger(values, "HISTORY_MAX_MESSAGES", "24")),
            new LlmRetryWindow(ParsePositiveInteger(values, "LLM_RETRY_WINDOW_SECONDS", "300")));
    }

    private int ParsePositiveInteger(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        var text = values.TryGetValue(key, out var configured) ? configured : fallback;
        if (!int.TryParse(text, out var value) || value <= 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration,
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

    private PortNumber ParsePort(string text, string key)
    {
        if (!int.TryParse(text, out var value) || value is < 1 or > 65535)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidPort,
                $"{key}: нужен номер порта от 1 до 65535.");
        }

        return new PortNumber(value);
    }
}
