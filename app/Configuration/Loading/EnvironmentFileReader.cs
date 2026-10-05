using ItmoBot.Configuration.Contracts;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Configuration.Loading;

public sealed class EnvironmentFileReader : IEnvironmentFileReader
{
    public EnvironmentReadResult Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var number = 0;
            foreach (var line in File.ReadLines(path))
            {
                number++;
                var text = line.Trim();

                if (text.Length == 0 || text.StartsWith('#'))
                {
                    continue;
                }

                if (text.StartsWith("export ", StringComparison.Ordinal))
                {
                    text = text[7..].TrimStart();
                }

                var separator = text.IndexOf('=');

                if (separator < 1)
                {
                    return InvalidLine(number, "ожидается строка KEY=VALUE.");
                }

                var key = text[..separator].Trim();

                if (Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_]*$") is false)
                {
                    return InvalidLine(number, "неверное имя настройки.");
                }

                var value = text[(separator + 1)..].Trim();

                if (value.StartsWith('\'') || value.StartsWith('"'))
                {
                    var end = value.IndexOf(value[0], 1);

                    if (end < 0)
                    {
                        return InvalidLine(number, "не закрыты кавычки значения.");
                    }

                    var tail = value[(end + 1)..].Trim();

                    if (tail.Length > 0 && !tail.StartsWith('#'))
                    {
                        return InvalidLine(number, "лишний текст после кавычек значения.");
                    }

                    value = value[1..end];
                }
                else
                {
                    var comment = value.IndexOf(" #", StringComparison.Ordinal);

                    if (comment >= 0)
                    {
                        value = value[..comment].TrimEnd();
                    }
                }

                values[key] = value;
            }
        }
        catch (FileNotFoundException)
        {
            // Допускается конфигурация только через окружение, как в Docker.
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new EnvironmentReadResult.Failure(new AppError(
                ErrorCode.EnvironmentFileUnavailable, "Не удалось прочитать env-файл. Проверьте путь и права доступа."));
        }

        return new EnvironmentReadResult.Success(new ReadOnlyDictionary<string, string>(values));
    }

    private EnvironmentReadResult InvalidLine(int number, string description)
    {
        return new EnvironmentReadResult.Failure(new AppError(
            ErrorCode.EnvironmentFileInvalid, $"ENV: строка {number}: {description}"));
    }
}
