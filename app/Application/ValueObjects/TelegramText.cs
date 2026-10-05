using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class TelegramText
{
    public TelegramText(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidMessageText, "Текст Telegram должен содержать от 1 до 4096 символов.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
