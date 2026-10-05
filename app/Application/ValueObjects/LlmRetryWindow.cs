using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class LlmRetryWindow
{
    public LlmRetryWindow(int seconds)
    {
        if (seconds is < 1 or > 600)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration,
                "LLM_RETRY_WINDOW_SECONDS: укажите целое число от 1 до 600.");
        }

        Value = TimeSpan.FromSeconds(seconds);
    }

    public TimeSpan Value
    {
        get;
    }
}
