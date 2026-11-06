using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Validation;

public static class TelegramChecks
{
    public static string CallbackQueryId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Некорректный идентификатор callback-запроса.");
        }

        return value;
    }

    public static int MessageId(int value)
    {
        if (value <= 0)
        {
            throw new InputValidationException(ErrorCode.InvalidMessageId, "Идентификатор сообщения должен быть положительным.");
        }

        return value;
    }

    public static string CodeLanguage(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '+' or '#')))
        {
            throw new InputValidationException(ErrorCode.InvalidCodeLanguage, "Некорректное обозначение языка кода.");
        }

        return value.ToLowerInvariant() switch
        {
            "c#" or "cs" => "csharp",
            "f#" => "fsharp",
            "js" => "javascript",
            "ts" => "typescript",
            "py" => "python",
            _ => value.ToLowerInvariant(),
        };
    }

    public static string TelegramCaption(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024)
        {
            throw new InputValidationException(ErrorCode.InvalidPhotoCaption, "Подпись фото должна содержать от 1 до 1024 символов.");
        }

        return value;
    }

    public static string TelegramText(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096)
        {
            throw new InputValidationException(ErrorCode.InvalidMessageText, "Текст Telegram должен содержать от 1 до 4096 символов.");
        }

        return value;
    }

    public static string BotUsername(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidBotUsername,
                "Telegram вернул пустое имя бота или имя с управляющими символами.");
        }

        return value;
    }

    public static string CallbackData(string value)
    {
        if (value is null || System.Text.Encoding.UTF8.GetByteCount(value) > 64)
        {
            throw new ArgumentException("Данные кнопки должны занимать не более 64 байт.");
        }

        return value;
    }

}
