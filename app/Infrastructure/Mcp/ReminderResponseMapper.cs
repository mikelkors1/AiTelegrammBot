using System.Globalization;
using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class ReminderResponseMapper
{
    public AgentStorageResult<ReminderCreation> Map(CallToolResult result, PreparedAction action)
    {
        try
        {
            var value = result.StructuredContent ?? throw new InvalidOperationException();
            if (result.IsError == true)
            {
                var code = value.GetProperty("error").GetProperty("code").GetString() switch
                {
                    "unauthorized" => ErrorCode.Unauthorized,
                    "invalid_arguments" => ErrorCode.InvalidArguments,
                    "action_not_confirmed" => ErrorCode.ActionNotConfirmed,
                    "action_expired" => ErrorCode.ActionExpired,
                    "action_cancelled" => ErrorCode.ActionCancelled,
                    "action_mismatch" => ErrorCode.ActionMismatch,
                    "storage_unavailable" => ErrorCode.DatabaseUnavailable,
                    _ => ErrorCode.InvalidMcpResponse,
                };
                return new AgentStorageResult<ReminderCreation>.Failure(new AppError(code,
                    "Не удалось выполнить подтверждённое действие.", value.GetProperty("error").GetProperty("retryable").GetBoolean()));
            }

            if (value.EnumerateObject().Count() != 4)
            {
                throw new InvalidOperationException();
            }

            var zone = new UserTimeZone(value.GetProperty("timezone").GetString()!);
            var moment = DateTimeOffset.ParseExact(value.GetProperty("remind_at").GetString()!, "O", CultureInfo.InvariantCulture);
            var payload = new ReminderPayload(action.Payload.Text, new ReminderSchedule(moment, zone));
            if (payload != action.Payload)
            {
                throw new InvalidOperationException();
            }

            var reminder = new StoredReminder(AgentChecks.ReminderId(value.GetProperty("reminder_id").GetGuid()), action.Id, action.Owner, payload);
            return new AgentStorageResult<ReminderCreation>.Success(new ReminderCreation(reminder, value.GetProperty("created").GetBoolean()));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or JsonException)
        {
            return new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.InvalidMcpResponse, "MCP-сервер вернул некорректный результат создания напоминания."));
        }
    }
}
