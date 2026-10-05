using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Context;

public sealed class ContextBuilder(IPromptCatalog prompts, IContextTokenEstimator estimator, ContextBudget budget) : IContextBuilder
{
    public ContextBuildResult Build(ConversationSnapshot conversation, LlmText currentMessage)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(currentMessage);
        var resolved = prompts.Resolve(conversation.State.Mode);
        if (resolved is PromptResolutionResult.Failure failure)
        {
            return new ContextBuildResult.Failure(failure.Error);
        }

        var history = conversation.History.ToArray();
        if (!IsCompleteHistory(history))
        {
            return new ContextBuildResult.Failure(new AppError(ErrorCode.InvalidContext,
                "История диалога повреждена. Сбросьте её командой /reset и повторите запрос."));
        }

        var definition = ((PromptResolutionResult.Success)resolved).Definition;
        var current = new LlmMessage(LlmMessageRole.User, currentMessage);
        var mandatory = Compose(definition, [], 0, current);
        if (!Fits(estimator.Estimate(mandatory)))
        {
            return new ContextBuildResult.Failure(new AppError(ErrorCode.ContextTooLarge,
                "Запрос слишком длинный для доступного контекста. Сократите текст и отправьте его снова."));
        }

        return IncludeHistory(definition, history, current, conversation.State.Temperature);
    }

    private ContextBuildResult IncludeHistory(PromptDefinition definition, LlmMessage[] history,
        LlmMessage current, Temperature temperature)
    {
        var allowedMessages = budget.HistoryMessages.Value / 2 * 2;
        var start = Math.Max(0, history.Length - allowedMessages);
        var messages = Compose(definition, history, start, current);
        var estimated = estimator.Estimate(messages);
        while (!Fits(estimated) && start < history.Length)
        {
            start += 2;
            messages = Compose(definition, history, start, current);
            estimated = estimator.Estimate(messages);
        }

        return new ContextBuildResult.Success(new PreparedLlmRequest(
            new LlmRequest(messages, temperature), definition.Version, estimated,
            new HistoryMessageCount(history.Length - start), new HistoryMessageCount(start)));
    }

    private bool Fits(EstimatedTokenCount estimated)
    {
        return estimated.Value <= budget.ContextTokens.Value - budget.OutputTokens.Value;
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
