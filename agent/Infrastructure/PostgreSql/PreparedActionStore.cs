using ItmoBot.Application.Agent.Models;
using ItmoBot.Shared.Validation;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class PreparedActionStore
{
    public Task<PreparedAction?> ReadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ActionOwner owner, Guid action, CancellationToken cancellationToken)
    {
        return ReadAsync(connection, transaction, owner, "action_id", action, cancellationToken);
    }

    public Task<PreparedAction?> ReadByUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ActionOwner owner, int update, CancellationToken cancellationToken)
    {
        return ReadAsync(connection, transaction, owner, "update_id", (long)update, cancellationToken);
    }

    public async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PreparedAction action, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO prepared_actions(action_id, owner_id, chat_id, update_id, reminder_text, remind_at,
                timezone, arguments_hash, status, created_at, expires_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'pending', $9, $10)
            """, connection, transaction);
        command.Parameters.AddWithValue(action.Id);
        command.Parameters.AddWithValue(action.Owner.OwnerId);
        command.Parameters.AddWithValue(action.Owner.ChatId);
        command.Parameters.AddWithValue((long)action.SourceUpdate);
        command.Parameters.AddWithValue(action.Payload.Text);
        command.Parameters.AddWithValue(action.Payload.Schedule.Moment.UtcDateTime);
        command.Parameters.AddWithValue(action.Payload.Schedule.TimeZone.Value.Id);
        command.Parameters.AddWithValue(Convert.FromHexString(action.ArgumentsHash));
        command.Parameters.AddWithValue(action.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue(action.ExpiresAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PreparedAction action, PreparedActionStatus status, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE prepared_actions SET status = $4 WHERE action_id = $1 AND owner_id = $2 AND chat_id = $3", connection, transaction);
        command.Parameters.AddWithValue(action.Id);
        command.Parameters.AddWithValue(action.Owner.OwnerId);
        command.Parameters.AddWithValue(action.Owner.ChatId);
        command.Parameters.AddWithValue(status.ToString().ToLowerInvariant());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CancelPendingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE prepared_actions SET status = 'cancelled' WHERE owner_id = $1 AND status = 'pending'", connection, transaction);
        command.Parameters.AddWithValue(owner);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<PreparedAction?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ActionOwner owner, string key, object value, CancellationToken cancellationToken)
    {
        // key задаётся только двумя методами этого класса, не аргументами модели.
        await using var command = new NpgsqlCommand($"""
            SELECT action_id, update_id, reminder_text, remind_at, timezone, arguments_hash, status, created_at, expires_at
            FROM prepared_actions WHERE owner_id = $1 AND chat_id = $2 AND {key} = $3 FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue(owner.OwnerId);
        command.Parameters.AddWithValue(owner.ChatId);
        command.Parameters.Add(new NpgsqlParameter { Value = value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var timezone = new UserTimeZone(reader.GetString(4));
        var schedule = new ReminderSchedule(timezone.ConvertInstant(reader.GetDateTime(3)), timezone);
        var status = Enum.Parse<PreparedActionStatus>(reader.GetString(6), true);
        return new PreparedAction(AgentChecks.PreparedActionId(reader.GetGuid(0)), owner, AgentChecks.UpdateId(checked((int)reader.GetInt64(1))),
            new ReminderPayload(AgentChecks.ReminderText(reader.GetString(2)), schedule),
            AgentChecks.ArgumentsHash(Convert.ToHexString(reader.GetFieldValue<byte[]>(5))), status, reader.GetDateTime(7), reader.GetDateTime(8));
    }
}
