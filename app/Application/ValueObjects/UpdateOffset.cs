using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class UpdateOffset
{
    public static readonly UpdateOffset Zero = new(0);

    public UpdateOffset(int value)
    {
        if (value < 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidUpdateOffset, "Смещение polling не может быть отрицательным.");
        }

        Value = value;
    }

    public int Value
    {
        get;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
