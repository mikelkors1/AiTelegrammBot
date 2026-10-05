using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class HistoryMessageCount
{
    public static readonly HistoryMessageCount Zero = new(0);

    public HistoryMessageCount(int value)
    {
        if (value < 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidContext, "Количество сообщений истории не может быть отрицательным.");
        }

        Value = value;
    }

    public int Value
    {
        get;
    }
}
