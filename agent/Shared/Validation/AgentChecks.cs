using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Shared.Validation;

public static class AgentChecks
{
    public static string McpToolName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.')))
        {
            throw new InputValidationException(ErrorCode.InvalidMcpResponse,
                "MCP-сервер вернул некорректное имя инструмента.");
        }

        return value;
    }

    public static int UpdateId(int value)
    {
        if (value < 0 || value == int.MaxValue)
        {
            throw new InputValidationException(ErrorCode.InvalidUpdateId, "Идентификатор обновления должен быть неотрицательным и допускать получение следующего смещения.");
        }

        return value;
    }

    public static int UpdateOffset(int value)
    {
        if (value < 0)
        {
            throw new InputValidationException(ErrorCode.InvalidUpdateOffset, "Смещение polling не может быть отрицательным.");
        }

        return value;
    }

    public static long ChatId(long value)
    {
        if (value == 0)
        {
            throw new InputValidationException(ErrorCode.InvalidChatId, "Идентификатор Telegram-чата не может быть равен нулю.");
        }

        return value;
    }

    public static Guid ReminderId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор напоминания не должен быть пустым.");
        }

        return value;
    }

    public static string ReminderText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 500)
        {
            throw new ArgumentException("Текст напоминания должен содержать от 1 до 500 символов.");
        }

        return value.Trim();
    }

    public static string AuditReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Причина должна содержать от 1 до 500 символов без управляющих символов.");
        }

        return value;
    }

    public static string AuditCity(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Название города должно содержать от 1 до 100 символов без управляющих символов.");
        }

        return value.Trim();
    }

    public static Guid PreparedActionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор действия не должен быть пустым.");
        }

        return value;
    }

    public static long OwnerId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentException("Идентификатор владельца должен быть положительным.");
        }

        return value;
    }

    public static string ArgumentsHash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("Ожидается хеш SHA-256 аргументов.");
        }

        return value.ToUpperInvariant();
    }

    public static Guid AgentRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор запроса не должен быть пустым.");
        }

        return value;
    }

}
