using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.ValueObjects;

public sealed record class LlmRequestTimeout
{
    public LlmRequestTimeout(int seconds)
    {
        if (seconds is < 1 or > 300)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration,
                "LLM_TIMEOUT_SECONDS: укажите целое число от 1 до 300.");
        }

        Value = TimeSpan.FromSeconds(seconds);
    }

    public TimeSpan Value
    {
        get;
    }
}
