using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class PreparedLlmRequest(
    LlmRequest Request,
    PromptVersion PromptVersion,
    EstimatedTokenCount EstimatedInputTokens,
    HistoryMessageCount IncludedHistoryMessages,
    HistoryMessageCount RemovedHistoryMessages);
