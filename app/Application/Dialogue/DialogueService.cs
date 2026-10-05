using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Dialogue;

public sealed class DialogueService(
    IConversationRepository repository,
    IContextBuilder context,
    ILlmClient llm,
    IProcessingNotifier progress,
    ILogger logger) : IDialogueService
{
    public async Task<DialogueResult> RespondAsync(ChatId chatId, LlmText text, CancellationToken cancellationToken)
    {
        var loaded = await repository.LoadAsync(chatId, cancellationToken);
        if (loaded is ConversationResult<ConversationSnapshot>.Failure loadFailure)
        {
            return new DialogueResult.Failure(loadFailure.Error);
        }

        var snapshot = ((ConversationResult<ConversationSnapshot>.Success)loaded).Value;
        var built = context.Build(snapshot, text);
        if (built is ContextBuildResult.Failure contextFailure)
        {
            return new DialogueResult.Failure(contextFailure.Error);
        }

        var started = await repository.BeginTurnAsync(chatId, text, cancellationToken);
        if (started is ConversationResult<ConversationTurn>.Failure startFailure)
        {
            return new DialogueResult.Failure(startFailure.Error);
        }

        return await RunTurnAsync(((ConversationResult<ConversationTurn>.Success)started).Value,
            snapshot.State, ((ContextBuildResult.Success)built).Prepared, cancellationToken);
    }

    private async Task<DialogueResult> RunTurnAsync(ConversationTurn turn, ConversationState expectedState,
        PreparedLlmRequest prepared, CancellationToken cancellationToken)
    {
        var completed = false;
        MessageId? notificationId = null;
        try
        {
            if (turn.State != expectedState)
            {
                return new DialogueResult.Failure(new AppError(ErrorCode.ConversationChanged, "Диалог изменился до начала генерации."));
            }

            var notified = await progress.NotifyAsync(turn.State.ChatId, cancellationToken);
            if (notified is TelegramSendResult.Failure progressFailure)
            {
                return new DialogueResult.Failure(progressFailure.Error);
            }

            notificationId = ((TelegramSendResult.Success)notified).MessageId;

            var generated = await llm.CompleteAsync(prepared.Request, cancellationToken);
            if (generated is LlmCompletionResult.Failure llmFailure)
            {
                return new DialogueResult.Failure(llmFailure.Error);
            }

            var response = ((LlmCompletionResult.Success)generated).Response;
            var saved = await repository.CompleteTurnAsync(turn.State.ChatId, turn.Id, response.Text, cancellationToken);
            if (saved is ConversationResult<ConversationTurnStatus>.Failure saveFailure)
            {
                return new DialogueResult.Failure(saveFailure.Error);
            }

            completed = true;
            logger.LogInformation("Диалог завершён: промпт {Version}, оценка входа {Estimate}, сообщений истории {Included}, исключено {Removed}.",
                prepared.PromptVersion.Value, prepared.EstimatedInputTokens.Value, prepared.IncludedHistoryMessages.Value, prepared.RemovedHistoryMessages.Value);
            return new DialogueResult.Success(new DialogueReply(response, prepared));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("Непредвиденная ошибка сценария диалога: {Code}.", ErrorCode.UnexpectedFailure);
            return new DialogueResult.Failure(new AppError(ErrorCode.UnexpectedFailure, "Не удалось завершить диалог."));
        }
        finally
        {
            if (notificationId is not null)
            {
                await DismissProgressAsync(turn.State.ChatId, notificationId);
            }

            if (!completed)
            {
                await MarkFailedAsync(turn);
            }
        }
    }

    private async Task DismissProgressAsync(ChatId chatId, MessageId messageId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var result = await progress.DismissAsync(chatId, messageId, cleanup.Token).WaitAsync(cleanup.Token);
            if (result is TelegramDeleteResult.Failure failure)
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
            if (result is ConversationResult<ConversationTurnStatus>.Failure failure
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
