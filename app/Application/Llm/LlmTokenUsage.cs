using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.Llm;

public sealed record class LlmTokenUsage
{
    public LlmTokenUsage(int inputTokens, int outputTokens, int totalTokens)
    {
        if (inputTokens < 0 || outputTokens < 0 || totalTokens < 0
            || (long)inputTokens + outputTokens != totalTokens)
        {
            throw new InputValidationException(ErrorCode.InvalidLlmResponse,
                "API вернул несогласованные значения использования токенов.");
        }

        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
    }

    public int InputTokens
    {
        get;
    }
    public int OutputTokens
    {
        get;
    }
    public int TotalTokens
    {
        get;
    }
}
