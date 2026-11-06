using System.Text.Json;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using Npgsql;
using NpgsqlTypes;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class PostgresAgentAuditRepository(IAgentTransactionExecutor executor, AgentUserStore users) : IAgentAuditRepository
{
    public Task<AgentStorageResult<AgentAuditEvent>> AppendEventAsync(AgentAuditEvent entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(entry.Action) || !Enum.IsDefined(entry.Status)
            || entry.Duration < TimeSpan.Zero || entry.Arguments.ReminderTextLength is < 0 or > 500
            || (entry.Action == AuditAction.ToolCall && entry.Tool is null)
            || (entry.Tool is not null && (entry.Tool.Length > 64 || entry.Tool.Contains('.'))))
        {
            return Task.FromResult<AgentStorageResult<AgentAuditEvent>>(new AgentStorageResult<AgentAuditEvent>.Failure(
                new AppError(ErrorCode.InvalidArguments, "Некорректные параметры события аудита.")));
        }

        return executor.ExecuteAsync<AgentAuditEvent>(async (connection, transaction, token) =>
        {
            await users.LockUserAsync(connection, transaction, entry.Owner, token);
            await using var command = new NpgsqlCommand("""
                INSERT INTO agent_audit_events(request_id, owner_id, chat_id, update_id, occurred_at,
                    action, tool_name, arguments_summary, status, reason, duration_ms)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
                """, connection, transaction);
            command.Parameters.AddWithValue(entry.RequestId);
            command.Parameters.AddWithValue(entry.Owner);
            command.Parameters.AddWithValue(entry.ChatId);
            command.Parameters.AddWithValue((long)entry.UpdateId);
            command.Parameters.AddWithValue(entry.OccurredAt.UtcDateTime);
            command.Parameters.AddWithValue(entry.Action == AuditAction.ToolCall ? "tool_call" : entry.Action.ToString().ToLowerInvariant());
            command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)entry.Tool ?? DBNull.Value);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new
            {
                reminder_text_length = entry.Arguments.ReminderTextLength,
                remind_at = entry.Arguments.RemindAt,
                timezone = entry.Arguments.TimeZone?.Value.Id,
                city = entry.Arguments.City,
                date = entry.Arguments.ScheduleDate,
            }));
            command.Parameters.AddWithValue(entry.Status == AuditStatus.LimitExceeded ? "limit_exceeded" : entry.Status.ToString().ToLowerInvariant());
            command.Parameters.AddWithValue(entry.Reason);
            command.Parameters.AddWithValue(NpgsqlDbType.Bigint, entry.Duration is { } duration ? (long)duration.TotalMilliseconds : DBNull.Value);
            await command.ExecuteNonQueryAsync(token);
            return new AgentStorageResult<AgentAuditEvent>.Success(entry);
        }, cancellationToken);
    }

    public Task<AgentStorageResult<AgentAuditEvent>> GetLatestEventAsync(long owner, long chat, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<AgentAuditEvent>(async (connection, transaction, token) =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT request_id, update_id, occurred_at, action, tool_name, arguments_summary::text, status, reason, duration_ms
                FROM agent_audit_events WHERE owner_id = $1 AND chat_id = $2 ORDER BY audit_id DESC LIMIT 1
                """, connection, transaction);
            command.Parameters.AddWithValue(owner);
            command.Parameters.AddWithValue(chat);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                return new AgentStorageResult<AgentAuditEvent>.NotFound();
            }

            var entry = new AgentAuditEvent(AgentChecks.AgentRequestId(reader.GetGuid(0)), owner, chat, AgentChecks.UpdateId(checked((int)reader.GetInt64(1))),
                reader.GetDateTime(2), reader.GetString(3) == "tool_call" ? AuditAction.ToolCall : Enum.Parse<AuditAction>(reader.GetString(3), true),
                reader.GetString(6) == "limit_exceeded" ? AuditStatus.LimitExceeded : Enum.Parse<AuditStatus>(reader.GetString(6), true),
                AgentChecks.AuditReason(reader.GetString(7)), ReadSummary(reader.GetString(5)),
                reader.IsDBNull(4) ? null : AgentChecks.McpToolName(reader.GetString(4)),
                reader.IsDBNull(8) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(8)));
            return new AgentStorageResult<AgentAuditEvent>.Success(entry);
        }, cancellationToken);
    }

    private AuditArgumentsSummary ReadSummary(string json)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement;
        value.TryGetProperty("reminder_text_length", out var length);
        value.TryGetProperty("remind_at", out var time);
        value.TryGetProperty("timezone", out var timezone);
        value.TryGetProperty("city", out var city);
        value.TryGetProperty("date", out var date);
        return new AuditArgumentsSummary(length.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : length.GetInt32(),
            time.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : time.GetDateTimeOffset(),
            timezone.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : new UserTimeZone(timezone.GetString()!),
            city.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : AgentChecks.AuditCity(city.GetString()!),
            date.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : date.Deserialize<DateOnly>());
    }
}
