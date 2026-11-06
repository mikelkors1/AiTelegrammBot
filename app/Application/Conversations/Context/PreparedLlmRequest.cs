using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.Conversations.Context;

public sealed record class PreparedLlmRequest
{
    public PreparedLlmRequest(LlmRequest request, string promptVersion, long estimatedInputTokens,
        int includedHistoryMessages, int removedHistoryMessages)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(promptVersion);
        if (estimatedInputTokens < 0 || includedHistoryMessages < 0 || removedHistoryMessages < 0)
        {
            throw new InputValidationException(ErrorCode.InvalidContext, "Счётчики подготовленного контекста не могут быть отрицательными.");
        }

        Request = request;
        PromptVersion = promptVersion;
        EstimatedInputTokens = estimatedInputTokens;
        IncludedHistoryMessages = includedHistoryMessages;
        RemovedHistoryMessages = removedHistoryMessages;
    }

    public LlmRequest Request
    {
        get;
    }
    public string PromptVersion
    {
        get;
    }
    public long EstimatedInputTokens
    {
        get;
    }
    public int IncludedHistoryMessages
    {
        get;
    }
    public int RemovedHistoryMessages
    {
        get;
    }
}
