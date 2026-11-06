using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Conversations.Context;

public sealed class ContextBuilder(PromptCatalog prompts, Utf8ContextTokenEstimator estimator, ContextBudget budget)
{
    public Result<PreparedLlmRequest> Build(ConversationSnapshot conversation, string currentMessage)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(currentMessage);
        Result<PromptDefinition> resolved = prompts.Resolve(conversation.State.Mode);
        if (resolved is Result<PromptDefinition>.Failure failure)
        {
            return new Result<PreparedLlmRequest>.Failure(failure.Error);
        }

        var history = conversation.History.ToArray();
        if (!IsCompleteHistory(history))
        {
            return new Result<PreparedLlmRequest>.Failure(new AppError(ErrorCode.InvalidContext,
                "История диалога повреждена. Сбросьте её командой /reset и повторите запрос."));
        }

        var definition = ((Result<PromptDefinition>.Success)resolved).Value;
        var current = new LlmMessage(LlmMessageRole.User, currentMessage);
        var mandatory = Compose(definition, [], 0, current);
        if (!Fits(estimator.Estimate(mandatory)))
        {
            return new Result<PreparedLlmRequest>.Failure(new AppError(ErrorCode.ContextTooLarge,
                "Запрос слишком длинный для доступного контекста. Сократите текст и отправьте его снова."));
        }

        return IncludeHistory(definition, history, current, conversation.State.Temperature);
    }

    private Result<PreparedLlmRequest> IncludeHistory(PromptDefinition definition, LlmMessage[] history,
        LlmMessage current, decimal temperature)
    {
        var allowedMessages = budget.HistoryMessages / 2 * 2;
        var start = Math.Max(0, history.Length - allowedMessages);
        var messages = Compose(definition, history, start, current);
        var estimated = estimator.Estimate(messages);
        while (!Fits(estimated) && start < history.Length)
        {
            start += 2;
            messages = Compose(definition, history, start, current);
            estimated = estimator.Estimate(messages);
        }

        return new Result<PreparedLlmRequest>.Success(new PreparedLlmRequest(
            new LlmRequest(messages, temperature, mandatoryMessageCount: definition.Messages.Count,
                historyMessageCount: history.Length - start), definition.Version, estimated,
            history.Length - start, start));
    }

    private bool Fits(long estimated)
    {
        return estimated <= budget.ContextTokens - budget.OutputTokens;
    }

    private bool IsCompleteHistory(LlmMessage[] history)
    {
        if (history.Length % 2 != 0)
        {
            return false;
        }

        for (var index = 0; index < history.Length; index += 2)
        {
            if (history[index] is null || history[index + 1] is null
                || history[index].Role != LlmMessageRole.User || history[index + 1].Role != LlmMessageRole.Assistant)
            {
                return false;
            }
        }

        return true;
    }

    private List<LlmMessage> Compose(PromptDefinition definition, LlmMessage[] history, int start, LlmMessage current)
    {
        var messages = new List<LlmMessage>(definition.Messages);
        for (var index = start; index < history.Length; index++)
        {
            messages.Add(history[index]);
        }

        messages.Add(current);
        return messages;
    }
}
