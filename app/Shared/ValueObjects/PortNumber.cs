using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Shared.ValueObjects;

public sealed record class PortNumber
{
    public PortNumber(int value)
    {
        if (value is < 1 or > 65535)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidPort, "Номер порта должен быть от 1 до 65535.");
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
