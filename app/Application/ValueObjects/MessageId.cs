using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class MessageId
{
    public MessageId(int value)
    {
        if (value <= 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidMessageId, "Идентификатор сообщения должен быть положительным.");
        }

        Value = value;
    }

    public int Value
    {
        get;
    }
}
