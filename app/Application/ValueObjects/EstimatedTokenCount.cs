using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class EstimatedTokenCount
{
    public static readonly EstimatedTokenCount Zero = new(0);

    public EstimatedTokenCount(long value)
    {
        if (value < 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidContext, "Оценка объёма контекста не может быть отрицательной.");
        }

        Value = value;
    }

    public long Value
    {
        get;
    }
}
