using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class TokenLimit
{
    public TokenLimit(int value)
    {
        if (value <= 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration, "Лимит токенов должен быть положительным целым числом.");
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
