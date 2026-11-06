using ItmoBot.Shared.Errors;

namespace ItmoBot.Shared.Exceptions;

public sealed class InputValidationException : ArgumentException
{
    public InputValidationException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code
    {
        get;
    }
}
