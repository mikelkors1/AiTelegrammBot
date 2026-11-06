using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.Conversations.Context;

public sealed class ContextBudget
{
    public ContextBudget(int contextTokens, int outputTokens, int historyMessages)
    {
        if (contextTokens <= 0 || outputTokens <= 0 || historyMessages <= 0 || outputTokens >= contextTokens)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "В контекстном бюджете должно оставаться место для входного запроса.");
        }

        ContextTokens = contextTokens;
        OutputTokens = outputTokens;
        HistoryMessages = historyMessages;
    }

    public int ContextTokens
    {
        get;
    }
    public int OutputTokens
    {
        get;
    }
    public int HistoryMessages
    {
        get;
    }
}
