using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Agent;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class PostgresPreparedActionRepository(IAgentTransactionExecutor executor, AgentUserStore users,
    PreparedActionStore actions, ReminderPayloadHasher hasher, PreparedActionPolicy policy, TimeProvider clock) : IPreparedActionRepository
{
    public Task<AgentStorageResult<PreparedAction>> PrepareReminderAsync(ActionOwner owner, int update, ReminderPayload payload, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<PreparedAction>(async (connection, transaction, token) =>
        {
            var timezone = await users.LockUserAsync(connection, transaction, owner.OwnerId, token);
            var previous = await actions.ReadByUpdateAsync(connection, transaction, owner, update, token);
            string hash = hasher.Hash(payload);
            if (previous is not null)
            {
                return previous.ArgumentsHash == hash
                    ? new AgentStorageResult<PreparedAction>.Success(previous)
                    : Failure(ErrorCode.ActionMismatch, "Для этого сообщения уже сохранены другие параметры действия.");
            }

            var now = clock.GetUtcNow();
            var error = policy.ValidatePreparation(timezone, payload, now);
            if (error is not null)
            {
                return new AgentStorageResult<PreparedAction>.Failure(error);
            }

            await actions.CancelPendingAsync(connection, transaction, owner.OwnerId, token);
            var action = new PreparedAction(AgentChecks.PreparedActionId(Guid.NewGuid()), owner, update, payload, hash,
                PreparedActionStatus.Pending, now, now.AddMinutes(5));
            await actions.InsertAsync(connection, transaction, action, token);
            return new AgentStorageResult<PreparedAction>.Success(action);
        }, cancellationToken);
    }

    public Task<AgentStorageResult<PreparedAction>> GetActionAsync(ActionOwner owner, Guid action, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<PreparedAction>(async (connection, transaction, token) =>
        {
            await users.LockUserAsync(connection, transaction, owner.OwnerId, token);
            var stored = await actions.ReadLockedAsync(connection, transaction, owner, action, token);
            return stored is null ? new AgentStorageResult<PreparedAction>.NotFound() : new AgentStorageResult<PreparedAction>.Success(stored);
        }, cancellationToken);
    }

    public Task<AgentStorageResult<PreparedAction>> ConfirmActionAsync(ActionOwner owner, Guid action, int confirmationUpdate, CancellationToken cancellationToken)
    {
        return ChangeAsync(owner, action, confirmationUpdate, cancellationToken);
    }

    public Task<AgentStorageResult<PreparedAction>> CancelActionAsync(ActionOwner owner, Guid action, CancellationToken cancellationToken)
    {
        return ChangeAsync(owner, action, null, cancellationToken);
    }

    private Task<AgentStorageResult<PreparedAction>> ChangeAsync(ActionOwner owner, Guid actionId, int? confirmationUpdate, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<PreparedAction>(async (connection, transaction, token) =>
        {
            await users.LockUserAsync(connection, transaction, owner.OwnerId, token);
            var action = await actions.ReadLockedAsync(connection, transaction, owner, actionId, token);
            if (action is null)
            {
                return new AgentStorageResult<PreparedAction>.NotFound();
            }

            var now = clock.GetUtcNow();
            var error = policy.ValidatePendingState(action, now);
            if (error is not null)
            {
                if (action.Status == PreparedActionStatus.Pending && error.Code == ErrorCode.ActionExpired)
                {
                    await actions.SetStatusAsync(connection, transaction, action, PreparedActionStatus.Expired, token);
                }

                return new AgentStorageResult<PreparedAction>.Failure(error);
            }

            if (action.Status is PreparedActionStatus.Confirmed or PreparedActionStatus.Executed)
            {
                return confirmationUpdate is not null
                    ? new AgentStorageResult<PreparedAction>.Success(action)
                    : Failure(ErrorCode.ActionAlreadyConfirmed, "Подтверждённое действие нельзя отменить как ожидающее.");
            }

            if (confirmationUpdate is not null && !await InsertConfirmationAsync(connection, transaction, action, confirmationUpdate.Value, now, token))
            {
                return Failure(ErrorCode.ActionMismatch, "Это подтверждение уже относится к другому действию.");
            }

            var status = confirmationUpdate is null ? PreparedActionStatus.Cancelled : PreparedActionStatus.Confirmed;
            await actions.SetStatusAsync(connection, transaction, action, status, token);
            return new AgentStorageResult<PreparedAction>.Success(action with
            {
                Status = status
            });
        }, cancellationToken);
    }

    private async Task<bool> InsertConfirmationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PreparedAction action,
        int update, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO action_confirmations(action_id, owner_id, chat_id, confirmation_update_id, confirmed_at)
            VALUES ($1, $2, $3, $4, $5) ON CONFLICT DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue(action.Id);
        command.Parameters.AddWithValue(action.Owner.OwnerId);
        command.Parameters.AddWithValue(action.Owner.ChatId);
        command.Parameters.AddWithValue((long)update);
        command.Parameters.AddWithValue(now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private AgentStorageResult<PreparedAction> Failure(ErrorCode code, string message)
    {
        return new AgentStorageResult<PreparedAction>.Failure(new AppError(code, message));
    }
}
