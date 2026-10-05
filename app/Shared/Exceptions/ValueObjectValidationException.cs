using ItmoBot.Shared.Errors;

namespace ItmoBot.Shared.Exceptions;

public sealed class ValueObjectValidationException : ArgumentException
{
    public ValueObjectValidationException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code
    {
        get;
    }
}
