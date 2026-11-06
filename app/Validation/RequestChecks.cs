using System.Text.Json;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Validation;

public static class RequestChecks
{
    public static int TokenLimit(int value)
    {
        if (value <= 0)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "Лимит токенов должен быть положительным целым числом.");
        }

        return value;
    }

    public static string LlmToolCallId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.' or ':')))
        {
            throw new ArgumentException("Некорректный идентификатор вызова инструмента.");
        }

        return value;
    }

    public static string ModelName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidModelName, "LLM_MODEL: укажите имя модели без пробелов и управляющих символов.");
        }

        return value;
    }

    public static string LlmProviderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InputValidationException(ErrorCode.InvalidLlmResponse, "API вернул некорректное имя провайдера.");
        }

        return value;
    }

    public static TimeSpan LlmRetryWindow(int seconds)
    {
        if (seconds is < 1 or > 600)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "LLM_RETRY_WINDOW_SECONDS: укажите целое число от 1 до 600.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static string LlmText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InputValidationException(ErrorCode.InvalidLlmText, "Текст для языковой модели не должен быть пустым.");
        }

        return value;
    }

    public static string McpResourceUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new InputValidationException(ErrorCode.InvalidMcpResponse,
                "MCP-сервер вернул некорректный URI ресурса.");
        }

        return value;
    }

    public static JsonElement McpObjectSchema(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String || type.GetString() != "object")
        {
            throw new InputValidationException(ErrorCode.InvalidMcpResponse,
                "Схемы инструментов проекта должны описывать JSON-объект.");
        }

        return value.Clone();
    }

    public static Guid ConversationTurnId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new InputValidationException(ErrorCode.InvalidConversationState, "Идентификатор хода диалога не должен быть пустым.");
        }

        return value;
    }

    public static decimal Temperature(decimal value)
    {
        if (value is not (0.0m or 0.3m or 0.7m or 1.0m))
        {
            throw new InputValidationException(ErrorCode.InvalidTemperature, "Коэффициент креативности ответа: допустимы 0.0, 0.3, 0.7, 1.0.");
        }

        return value;
    }

    public static string FormatTemperature(decimal value)
    {
        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static long ConversationRevision(long value)
    {
        if (value < 0)
        {
            throw new InputValidationException(ErrorCode.InvalidConversationState, "Версия контекста не может быть отрицательной.");
        }

        return value;
    }

    public static int HistoryMessageLimit(int value)
    {
        if (value <= 0)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "Лимит истории должен быть положительным целым числом.");
        }

        return value;
    }

    public static string PromptVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new InputValidationException(ErrorCode.InvalidPrompt, "Версия инструкции должна содержать латинские буквы, цифры и дефисы.");
        }

        return value;
    }

}
