using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class MessagePartCount
{
    public static readonly MessagePartCount Zero = new(0);

    public MessagePartCount(int value)
    {
        if (value < 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidMessageText, "Количество частей сообщения не может быть отрицательным.");
        }

        Value = value;
    }

    public int Value
    {
        get;
    }
}
