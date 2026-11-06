using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Conversations;

public sealed class DialogueService(
    IConversationRepository repository,
    ContextBuilder context,
    ILlmClient llm,
    ProcessingNotifier progress,
    ILogger logger, AgentCompletionService? agent = null) : IDialogueService
{
    public async Task<Result<DialogueReply>> RespondAsync(long chatId, string text, CancellationToken cancellationToken, AgentTurnContext? turnContext = null)
    {
        var loaded = await repository.LoadAsync(chatId, cancellationToken);
        if (loaded is Result<ConversationSnapshot>.Failure loadFailure)
        {
            return new Result<DialogueReply>.Failure(loadFailure.Error);
        }

        var snapshot = ((Result<ConversationSnapshot>.Success)loaded).Value;
        Result<PreparedLlmRequest> built = context.Build(snapshot, text);
        if (built is Result<PreparedLlmRequest>.Failure contextFailure)
        {
            return new Result<DialogueReply>.Failure(contextFailure.Error);
        }

        var started = await repository.BeginTurnAsync(chatId, text, cancellationToken);
        if (started is Result<ConversationTurn>.Failure startFailure)
        {
            return new Result<DialogueReply>.Failure(startFailure.Error);
        }

        return await RunTurnAsync(((Result<ConversationTurn>.Success)started).Value,
            snapshot.State, ((Result<PreparedLlmRequest>.Success)built).Value, cancellationToken, turnContext);
    }

    private async Task<Result<DialogueReply>> RunTurnAsync(ConversationTurn turn, ConversationState expectedState,
        PreparedLlmRequest prepared, CancellationToken cancellationToken, AgentTurnContext? turnContext)
    {
        var completed = false;
        int? notificationId = null;
        try
        {
            if (turn.State != expectedState)
            {
                return new Result<DialogueReply>.Failure(new AppError(ErrorCode.ConversationChanged, "Диалог изменился до начала генерации."));
            }

            Result<int> notified = await progress.NotifyAsync(turn.State.ChatId, cancellationToken);
            if (notified is Result<int>.Failure progressFailure)
            {
                return new Result<DialogueReply>.Failure(progressFailure.Error);
            }

            notificationId = ((Result<int>.Success)notified).Value;

            ItmoBot.Application.Agent.Models.PreparedAction? pending = null;
            LlmCompletionResult generated;
            if (agent is not null)
            {
                var result = await agent.CompleteAsync(prepared.Request, turnContext, cancellationToken);
                if (result is AgentCompletionResult.Failure agentFailure)
                {
                    return new Result<DialogueReply>.Failure(agentFailure.Error);
                }

                var success = (AgentCompletionResult.Success)result;
                pending = success.PendingAction;
                generated = new LlmCompletionResult.Success(success.Response);
            }
            else
            {
                generated = await llm.CompleteAsync(prepared.Request, cancellationToken);
            }
            if (generated is LlmCompletionResult.Failure llmFailure)
            {
                return new Result<DialogueReply>.Failure(llmFailure.Error);
            }

            var response = ((LlmCompletionResult.Success)generated).Response;
            var saved = await repository.CompleteTurnAsync(turn.State.ChatId, turn.Id, response.Text, cancellationToken);
            if (saved is Result<ConversationTurnStatus>.Failure saveFailure)
            {
                return new Result<DialogueReply>.Failure(saveFailure.Error);
            }

            completed = true;
            logger.LogInformation("Диалог завершён: промпт {Version}, оценка входа {Estimate}, сообщений истории {Included}, исключено {Removed}.",
                prepared.PromptVersion, prepared.EstimatedInputTokens, prepared.IncludedHistoryMessages, prepared.RemovedHistoryMessages);
            return new Result<DialogueReply>.Success(new DialogueReply(response, prepared, pending));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("Непредвиденная ошибка сценария диалога: {Code}.", ErrorCode.UnexpectedFailure);
            return new Result<DialogueReply>.Failure(new AppError(ErrorCode.UnexpectedFailure, "Не удалось завершить диалог."));
        }
        finally
        {
            if (notificationId is not null)
            {
                await DismissProgressAsync(turn.State.ChatId, notificationId.Value);
            }

            if (!completed)
            {
                await MarkFailedAsync(turn);
            }
        }
    }

    private async Task DismissProgressAsync(long chatId, int messageId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var result = await progress.DismissAsync(chatId, messageId, cleanup.Token).WaitAsync(cleanup.Token);
            if (result is Result.Failure failure)
            {
                logger.LogWarning("Не удалось удалить уведомление ожидания: {Code}.", failure.Error.Code);
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Не удалось удалить уведомление ожидания.");
        }
    }

    private async Task MarkFailedAsync(ConversationTurn turn)
    {
        // Caller cancellation must not leave the failed attempt pending when the DB is available.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var result = await repository.FailTurnAsync(turn.State.ChatId, turn.Id, cleanup.Token);
            if (result is Result<ConversationTurnStatus>.Failure failure
                && failure.Error.Code is not (ErrorCode.ConversationTurnNotFound or ErrorCode.ConversationTurnAlreadyFinished))
            {
                logger.LogWarning("Не удалось отметить неудачный запрос: {Code}.", failure.Error.Code);
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Не удалось завершить сохранение статуса неудачного запроса.");
        }
    }
}
