using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class ChatId
{
    public ChatId(long value)
    {
        if (value == 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidChatId, "Идентификатор Telegram-чата не может быть равен нулю.");
        }

        Value = value;
    }

    public long Value
    {
        get;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
