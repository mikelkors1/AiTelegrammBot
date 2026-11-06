using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed class PostgresConversationRepository(
    PostgresOperationExecutor executor,
    ConversationStateStore stateStore,
    ConversationTurnStore turnStore,
    ConversationHistoryStore historyStore) : IConversationRepository
{
    public Task<Result<ConversationSnapshot>> LoadAsync(long chatId, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationSnapshot>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var history = await historyStore.ReadAsync(connection, transaction, chatId, token);
            return new Result<ConversationSnapshot>.Success(new ConversationSnapshot(state, history));
        }, cancellationToken);
    }

    public Task<Result<ConversationState>> SetModeAsync(long chatId, AssistantMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode))
        {
            return Task.FromResult<Result<ConversationState>>(new Result<ConversationState>.Failure(
                new AppError(ErrorCode.InvalidConversationMode, "Выбран неизвестный режим ассистента.")));
        }

        return ClearAsync(chatId, mode, cancellationToken);
    }

    public Task<Result<ConversationState>> SetTemperatureAsync(long chatId, decimal temperature, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationState>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var changed = await stateStore.SetTemperatureAsync(connection, transaction, state, temperature, token);
            return new Result<ConversationState>.Success(changed);
        }, cancellationToken);
    }

    public Task<Result<ConversationState>> ResetAsync(long chatId, CancellationToken cancellationToken)
    {
        return ClearAsync(chatId, null, cancellationToken);
    }

    public Task<Result<ConversationTurn>> BeginTurnAsync(long chatId, string userText, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationTurn>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            Guid turnId = await turnStore.BeginAsync(connection, transaction, state, token);
            await historyStore.AppendAsync(connection, transaction, turnId, new LlmMessage(LlmMessageRole.User, userText), token);
            return new Result<ConversationTurn>.Success(new ConversationTurn(turnId, state));
        }, cancellationToken);
    }

    public Task<Result<ConversationTurnStatus>> CompleteTurnAsync(long chatId, Guid turnId,
        string assistantText, CancellationToken cancellationToken)
    {
        return FinishTurnAsync(chatId, turnId, assistantText, cancellationToken);
    }

    public Task<Result<ConversationTurnStatus>> FailTurnAsync(long chatId, Guid turnId, CancellationToken cancellationToken)
    {
        return FinishTurnAsync(chatId, turnId, null, cancellationToken);
    }

    private Task<Result<ConversationState>> ClearAsync(long chatId, AssistantMode? mode, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationState>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var changed = await stateStore.ClearAsync(connection, transaction, state, mode ?? state.Mode, token);
            return new Result<ConversationState>.Success(changed);
        }, cancellationToken);
    }

    private Task<Result<ConversationTurnStatus>> FinishTurnAsync(long chatId, Guid turnId,
        string? assistantText, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ConversationTurnStatus>(async (connection, transaction, token) =>
        {
            var state = await stateStore.LoadLockedAsync(connection, transaction, chatId, token);
            var turn = await turnStore.ReadLockedAsync(connection, transaction, chatId, turnId, token);
            var validationError = ValidatePendingTurn(turn, state);
            if (validationError is not null)
            {
                return new Result<ConversationTurnStatus>.Failure(validationError);
            }

            var status = await SaveOutcomeAsync(connection, transaction, turnId, assistantText, token);
            return new Result<ConversationTurnStatus>.Success(status);
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
        Guid turnId, string? assistantText, CancellationToken cancellationToken)
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
