using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.Models;

public sealed record class LlmTokenUsage
{
    public LlmTokenUsage(TokenCount inputTokens, TokenCount outputTokens, TokenCount totalTokens)
    {
        if ((long)inputTokens.Value + outputTokens.Value != totalTokens.Value)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidLlmResponse,
                "API вернул несогласованные значения использования токенов.");
        }

        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
    }

    public TokenCount InputTokens
    {
        get;
    }
    public TokenCount OutputTokens
    {
        get;
    }
    public TokenCount TotalTokens
    {
        get;
    }
}
