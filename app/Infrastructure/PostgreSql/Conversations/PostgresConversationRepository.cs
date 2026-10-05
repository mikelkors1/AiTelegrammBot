using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Shared.Errors;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed class PostgresConversationRepository(
    IPostgresOperationExecutor executor,
    IConversationStateStore stateStore,
    IConversationTurnStore turnStore,
    IConversationHistoryStore historyStore) : IConversationRepository
{
    public Task<ConversationResult<ConversationSnapshot>> LoadAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationSnapshot>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            await turnStore.PruneAsync(connection, transaction, chatId, token);
            var history = await historyStore.ReadAsync(connection, transaction, chatId, token);
            return new ConversationResult<ConversationSnapshot>.Success(new ConversationSnapshot(state, history));
        }, cancellationToken);
    }

    public Task<ConversationResult<ConversationState>> SetModeAsync(ChatId chatId, AssistantMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode))
        {
            return Task.FromResult<ConversationResult<ConversationState>>(new ConversationResult<ConversationState>.Failure(
                new AppError(ErrorCode.InvalidConversationMode, "Выбран неизвестный режим ассистента.")));
        }

        return ClearAsync(chatId, mode, cancellationToken);
    }

    public Task<ConversationResult<ConversationState>> SetTemperatureAsync(ChatId chatId, Temperature temperature, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationState>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var changed = await stateStore.SetTemperatureAsync(connection, transaction, state, temperature, token);
            return new ConversationResult<ConversationState>.Success(changed);
        }, cancellationToken);
    }

    public Task<ConversationResult<ConversationState>> ResetAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        return ClearAsync(chatId, null, cancellationToken);
    }

    public Task<ConversationResult<ConversationTurn>> BeginTurnAsync(ChatId chatId, LlmText userText, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationTurn>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var turnId = await turnStore.BeginAsync(connection, transaction, state, token);
            await historyStore.AppendAsync(connection, transaction, turnId, new LlmMessage(LlmMessageRole.User, userText), token);
            await turnStore.PruneAsync(connection, transaction, chatId, token);
            return new ConversationResult<ConversationTurn>.Success(new ConversationTurn(turnId, state));
        }, cancellationToken);
    }

    public Task<ConversationResult<ConversationTurnStatus>> CompleteTurnAsync(ChatId chatId, ConversationTurnId turnId,
        LlmText assistantText, CancellationToken cancellationToken)
    {
        return FinishTurnAsync(chatId, turnId, assistantText, cancellationToken);
    }

    public Task<ConversationResult<ConversationTurnStatus>> FailTurnAsync(ChatId chatId, ConversationTurnId turnId, CancellationToken cancellationToken)
    {
        return FinishTurnAsync(chatId, turnId, null, cancellationToken);
    }

    private Task<ConversationResult<ConversationState>> ClearAsync(ChatId chatId, AssistantMode? mode, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationState>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var changed = await stateStore.ClearAsync(connection, transaction, state, mode ?? state.Mode, token);
            return new ConversationResult<ConversationState>.Success(changed);
        }, cancellationToken);
    }

    private Task<ConversationResult<ConversationTurnStatus>> FinishTurnAsync(ChatId chatId, ConversationTurnId turnId,
        LlmText? assistantText, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationTurnStatus>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var turn = await turnStore.ReadLockedAsync(connection, transaction, chatId, turnId, token);
            var validationError = ValidatePendingTurn(turn, state);
            if (validationError is not null)
            {
                return new ConversationResult<ConversationTurnStatus>.Failure(validationError);
            }

            var status = await SaveOutcomeAsync(connection, transaction, turnId, assistantText, token);
            return new ConversationResult<ConversationTurnStatus>.Success(status);
        }, cancellationToken);
    }

    private AppError? ValidatePendingTurn(StoredConversationTurn? turn, ConversationState state)
    {
        if (turn is null)
        {
            return new AppError(ErrorCode.ConversationTurnNotFound, "Этот запрос больше не относится к текущему диалогу.");
        }

        if (turn.Revision != state.Revision)
        {
            return new AppError(ErrorCode.ConversationChanged, "Контекст диалога изменился. Запоздалый ответ не сохранён.");
        }

        if (turn.Status != ConversationTurnStatus.Pending)
        {
            return new AppError(ErrorCode.ConversationTurnAlreadyFinished, "Этот запрос уже завершён; повторный ответ не сохранён.");
        }

        return null;
    }

    private async Task<ConversationTurnStatus> SaveOutcomeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ConversationTurnId turnId, LlmText? assistantText, CancellationToken cancellationToken)
    {
        var status = ConversationTurnStatus.Failed;
        if (assistantText is not null)
        {
            await historyStore.AppendAsync(connection, transaction, turnId, new LlmMessage(LlmMessageRole.Assistant, assistantText), cancellationToken);
            status = ConversationTurnStatus.Succeeded;
        }

        await turnStore.SetStatusAsync(connection, transaction, turnId, status, cancellationToken);
        return status;
    }
}
