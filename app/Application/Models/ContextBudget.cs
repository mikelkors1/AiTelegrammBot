using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.Models;

public sealed class ContextBudget
{
    public ContextBudget(TokenLimit contextTokens, TokenLimit outputTokens, HistoryMessageLimit historyMessages)
    {
        ArgumentNullException.ThrowIfNull(contextTokens);
        ArgumentNullException.ThrowIfNull(outputTokens);
        ArgumentNullException.ThrowIfNull(historyMessages);
        if (outputTokens.Value >= contextTokens.Value)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration, "В контекстном бюджете должно оставаться место для входного запроса.");
        }

        ContextTokens = contextTokens;
        OutputTokens = outputTokens;
        HistoryMessages = historyMessages;
    }

    public TokenLimit ContextTokens
    {
        get;
    }
    public TokenLimit OutputTokens
    {
        get;
    }
    public HistoryMessageLimit HistoryMessages
    {
        get;
    }
}
