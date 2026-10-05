using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.ValueObjects;

public sealed record class DatabaseName
{
    public DatabaseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration,
                "POSTGRES_DB: укажите непустое значение без управляющих символов.");
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
