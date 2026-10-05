using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.ValueObjects;

public sealed record class DatabaseUser
{
    public DatabaseUser(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration,
                "POSTGRES_USER: укажите непустое значение без управляющих символов.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }

    public override string ToString()
    {
        return Value;
    }
}
