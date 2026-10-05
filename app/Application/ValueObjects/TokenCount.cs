using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class TokenCount
{
    public static readonly TokenCount Zero = new(0);

    public TokenCount(int value)
    {
        if (value < 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidLlmResponse, "Число токенов не может быть отрицательным.");
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
