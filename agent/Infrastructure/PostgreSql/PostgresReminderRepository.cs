using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Agent;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class PostgresReminderRepository(IAgentTransactionExecutor executor, AgentUserStore users,
    PreparedActionStore actions, ReminderPayloadHasher hasher, PreparedActionPolicy policy, TimeProvider clock) : IReminderRepository
{
    public Task<AgentStorageResult<ReminderCreation>> ExecuteConfirmedActionAsync(ActionOwner owner, Guid actionId, string expectedArguments, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<ReminderCreation>(async (connection, transaction, token) =>
        {
            await users.LockUserAsync(connection, transaction, owner.OwnerId, token);
            var action = await actions.ReadLockedAsync(connection, transaction, owner, actionId, token);
            if (action is null)
            {
                return new AgentStorageResult<ReminderCreation>.NotFound();
            }

            if (action.ArgumentsHash != expectedArguments || hasher.Hash(action.Payload) != expectedArguments)
            {
                return Failure(ErrorCode.ActionMismatch, "Параметры не совпадают с подготовленным действием.");
            }

            if (action.Status == PreparedActionStatus.Executed)
            {
                var previous = await ReadAsync(connection, transaction, owner, "action_id", actionId, token)
                    ?? throw new InvalidOperationException("Отсутствует результат выполненного действия.");
                return new AgentStorageResult<ReminderCreation>.Success(new ReminderCreation(previous, false));
            }

            var now = clock.GetUtcNow();
            var error = policy.ValidatePendingState(action, now);
            if (error is not null)
            {
                if (action.Status == PreparedActionStatus.Pending && error.Code == ErrorCode.ActionExpired)
                {
                    await actions.SetStatusAsync(connection, transaction, action, PreparedActionStatus.Expired, token);
                }

                return new AgentStorageResult<ReminderCreation>.Failure(error);
            }

            if (action.Status != PreparedActionStatus.Confirmed)
            {
                return Failure(ErrorCode.ActionNotConfirmed, "Сначала подтвердите создание напоминания.");
            }

            if (action.Payload.Schedule.Moment <= now)
            {
                return Failure(ErrorCode.ReminderTimeInPast, "Время напоминания уже прошло.");
            }

            var reminder = new StoredReminder(AgentChecks.ReminderId(Guid.NewGuid()), actionId, owner, action.Payload);
            await InsertAsync(connection, transaction, reminder, now, token);
            await actions.SetStatusAsync(connection, transaction, action, PreparedActionStatus.Executed, token);
            return new AgentStorageResult<ReminderCreation>.Success(new ReminderCreation(reminder, true));
        }, cancellationToken);
    }

    public Task<AgentStorageResult<StoredReminder>> GetReminderAsync(ActionOwner owner, Guid reminder, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<StoredReminder>(async (connection, transaction, token) =>
        {
            var stored = await ReadAsync(connection, transaction, owner, "reminder_id", reminder, token);
            return stored is null ? new AgentStorageResult<StoredReminder>.NotFound() : new AgentStorageResult<StoredReminder>.Success(stored);
        }, cancellationToken);
    }

    private async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StoredReminder reminder, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO reminders(reminder_id, action_id, owner_id, chat_id, reminder_text, remind_at, timezone, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """, connection, transaction);
        command.Parameters.AddWithValue(reminder.Id);
        command.Parameters.AddWithValue(reminder.ActionId);
        command.Parameters.AddWithValue(reminder.Owner.OwnerId);
        command.Parameters.AddWithValue(reminder.Owner.ChatId);
        command.Parameters.AddWithValue(reminder.Payload.Text);
        command.Parameters.AddWithValue(reminder.Payload.Schedule.Moment.UtcDateTime);
        command.Parameters.AddWithValue(reminder.Payload.Schedule.TimeZone.Value.Id);
        command.Parameters.AddWithValue(now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<StoredReminder?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ActionOwner owner, string key, Guid id, CancellationToken cancellationToken)
    {
        // key поступает только из методов этого класса.
        await using var command = new NpgsqlCommand($"""
            SELECT reminder_id, action_id, reminder_text, remind_at, timezone FROM reminders
            WHERE owner_id = $1 AND chat_id = $2 AND {key} = $3
            """, connection, transaction);
        command.Parameters.AddWithValue(owner.OwnerId);
        command.Parameters.AddWithValue(owner.ChatId);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var timezone = new UserTimeZone(reader.GetString(4));
        var payload = new ReminderPayload(AgentChecks.ReminderText(reader.GetString(2)), new ReminderSchedule(timezone.ConvertInstant(reader.GetDateTime(3)), timezone));
        return new StoredReminder(AgentChecks.ReminderId(reader.GetGuid(0)), AgentChecks.PreparedActionId(reader.GetGuid(1)), owner, payload);
    }

    private AgentStorageResult<ReminderCreation> Failure(ErrorCode code, string message)
    {
        return new AgentStorageResult<ReminderCreation>.Failure(new AppError(code, message));
    }
}
