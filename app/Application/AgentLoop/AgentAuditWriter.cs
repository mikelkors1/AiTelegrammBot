using System.Text.Json;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Validation;

namespace ItmoBot.Application.AgentLoop;

public sealed class AgentAuditWriter(IAgentAuditRepository audit, TimeProvider clock, ILogger logger) : IAgentAuditWriter
{
    public async Task RecordAsync(AgentTurnContext? context, Guid request, AuditAction action, AuditStatus status, LlmToolCall? call, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var summary = Summary(call, context.TimeZone);

        string? tool = call is not null && call.Name.Length <= 64 && !call.Name.Contains('.') ? call.Name : null;
        if (action == AuditAction.ToolCall && tool is null)
        {
            action = AuditAction.Reject;
        }

        var reason = action switch
        {
            AuditAction.Respond => "Ответ без вызова инструмента.",
            AuditAction.Clarify => "Требуется уточнение параметров или подтверждение пользователя.",
            AuditAction.ToolCall => "Проверенный вызов инструмента; результат и статус сохранены.",
            _ => "Действие отклонено проверкой или ограничением агентного цикла.",
        };
        if (context.DecisionReason is not null && action is AuditAction.Respond or AuditAction.Clarify)
        {
            reason = context.DecisionReason;
        }

        var entry = new AgentAuditEvent(request, context.Owner, context.ChatId, context.UpdateId, clock.GetUtcNow(),
            action, status, AgentChecks.AuditReason(reason), summary, tool,
            context.StartedAt is { } started && clock.GetUtcNow() >= started ? clock.GetUtcNow() - started : TimeSpan.Zero);
        var saved = await audit.AppendEventAsync(entry, cancellationToken);
        if (saved is AgentStorageResult<AgentAuditEvent>.Failure failure)
        {
            logger.LogWarning("Не удалось сохранить аудит агента: {Code}.", failure.Error.Code);
        }
    }

    private AuditArgumentsSummary Summary(LlmToolCall? call, UserTimeZone? timezone)
    {
        if (call is null)
        {
            return new AuditArgumentsSummary();
        }

        try
        {
            if (call.Name == "get_weather" && call.Arguments.TryGetProperty("city", out var city) && city.ValueKind == JsonValueKind.String)
            {
                return new AuditArgumentsSummary(City: AgentChecks.AuditCity(city.GetString()!));
            }

            if (call.Name == "get_schedule" && call.Arguments.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
            {
                return new AuditArgumentsSummary(ScheduleDate: day);
            }

            if (call.Name == "add_reminder" && call.Arguments.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                DateTimeOffset? moment = null;
                if (call.Arguments.TryGetProperty("remind_at", out var time) && time.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(time.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    moment = parsed;
                }

                return new AuditArgumentsSummary(Math.Min(500, text.GetString()!.Length), moment, timezone);
            }
        }
        catch (ArgumentException)
        {
            // Неверные аргументы не должны мешать записи самого факта отказа.
        }

        return new AuditArgumentsSummary();
    }

}
