using System.Collections;
using System.Security.Cryptography;
using System.Text;
using ItmoBot.Configuration.Contracts;
using ItmoBot.Tools.Contracts;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Tools.ResultTypes;

namespace ItmoBot.Tools;

public sealed class EnvironmentPreparer(
    IEnvironmentFileReader environmentFileReader,
    ISettingsLoader settingsLoader,
    ISecretConsoleInput secretInput) : IEnvironmentPreparer
{
    public EnvironmentPreparationResult Prepare(string path = ".env")
    {
        var read = environmentFileReader.Read(path);
        if (read is EnvironmentReadResult.Failure readFailure)
        {
            return new EnvironmentPreparationResult.Failure(readFailure.Error);
        }

        var values = ((EnvironmentReadResult.Success)read).Values;
        var additions = new Dictionary<string, string>();
        string Value(string key)
        {
            return Environment.GetEnvironmentVariable(key) ?? values.GetValueOrDefault(key, "");
        }

        var defaults = new Dictionary<string, string>
        {
            ["POSTGRES_HOST"] = "127.0.0.1",
            ["POSTGRES_PORT"] = "5432",
            ["POSTGRES_DB"] = "bot",
            ["POSTGRES_USER"] = "bot",
            ["LOG_LEVEL"] = "INFO",
            ["HEALTH_PORT"] = "8080",
            ["LLM_API_BASE_URL"] = "https://openrouter.ai/api/v1/",
            ["LLM_MODEL"] = "qwen/qwen3.8-27b:free",
            ["LLM_RETRY_WINDOW_SECONDS"] = "300",
        };
        foreach (var (key, fallback) in defaults)
        {
            if (Value(key).Length == 0)
            {
                additions[key] = fallback;
            }
        }

        if (Value("BOT_TOKEN").Length == 0)
        {
            additions["BOT_TOKEN"] = secretInput.Read("Токен бота из BotFather (ввод скрыт): ");
        }

        if (Value("OPENROUTER_API_KEY").Length == 0)
        {
            additions["OPENROUTER_API_KEY"] = secretInput.Read("Ключ OpenRouter (ввод скрыт): ");
        }

        if (Value("POSTGRES_PASSWORD").Length == 0)
        {
            additions["POSTGRES_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        }

        if (!values.ContainsKey("TELEGRAM_PROXY_URL") && Environment.GetEnvironmentVariable("TELEGRAM_PROXY_URL") is null)
        {
            additions["TELEGRAM_PROXY_URL"] = secretInput.Read("URL HTTP/SOCKS5 прокси (Enter — без прокси): ");
        }

        var merged = new Dictionary<string, string>(values);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            merged[(string)entry.Key] = (string)entry.Value!;
        }

        foreach (var entry in additions)
        {
            merged[entry.Key] = entry.Value;
        }

        var loaded = settingsLoader.Load(path, merged);
        if (loaded is SettingsLoadResult.Failure settingsFailure)
        {
            return new EnvironmentPreparationResult.Failure(settingsFailure.Error);
        }

        try
        {
            var content = File.Exists(path) ? File.ReadAllText(path) + "\n" : "# Секретные настройки. Не добавляйте в Git.\n";
            foreach (var (key, value) in additions)
            {
                var quote = value.Contains('\'') ? '"' : '\'';
                if (value.Contains(quote) || value.Contains('\n') || value.Contains('\r'))
                {
                    return new EnvironmentPreparationResult.Failure(new AppError(
                        ErrorCode.EnvironmentFileInvalid, "ENV: значение содержит неподдерживаемые кавычки или перевод строки."));
                }

                content += $"{key}={quote}{value}{quote}\n";
            }

            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var file = new FileStream(path, options);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            file.SetLength(0);
            file.Write(Encoding.UTF8.GetBytes(content));
            return new EnvironmentPreparationResult.Success();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new EnvironmentPreparationResult.Failure(new AppError(
                ErrorCode.EnvironmentFileUnavailable, "Не удалось сохранить env-файл. Проверьте права доступа к каталогу проекта."));
        }
    }
}
