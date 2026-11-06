using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.Secrets;

public sealed record class SecretValue
{
    public SecretValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "POSTGRES_PASSWORD: пароль базы данных не задан.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }

    public override string ToString()
    {
        return "[скрыто]";
    }
}
