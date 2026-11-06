using System.Globalization;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;

namespace ItmoBot.Application.Reminders;

public sealed class ReminderConfirmationService(IPreparedActionRepository actions, IMcpRuntime runtime,
    IAgentAuditRepository audit, TimeProvider clock, ILogger logger)
{
    public async Task<ReminderInteractionResult> ExecuteAsync(AgentTurnContext context, ReminderButtonAction button, CancellationToken cancellationToken)
    {
        if (context.Owner != context.ChatId)
        {
            return new ReminderInteractionResult.Failure(new AppError(ErrorCode.Unauthorized, "Действие доступно только владельцу в личном чате."));
        }

        var owner = new ActionOwner(context.Owner, context.ChatId);
        var loaded = await actions.GetActionAsync(owner, button.ActionId, cancellationToken);
        if (loaded is AgentStorageResult<PreparedAction>.Failure unavailable)
        {
            return Failure(unavailable.Error);
        }

        if (loaded is not AgentStorageResult<PreparedAction>.Success original)
        {
            var rejected = new AgentAuditEvent(AgentChecks.AgentRequestId(Guid.NewGuid()), context.Owner, context.ChatId,
                context.UpdateId, clock.GetUtcNow(), AuditAction.ToolCall, AuditStatus.Rejected,
                AgentChecks.AuditReason("Запрошено недоступное действие; чужие параметры не раскрываются."),
                new AuditArgumentsSummary(), AgentChecks.McpToolName("add_reminder"), TimeSpan.Zero);
            var recorded = await audit.AppendEventAsync(rejected, cancellationToken);
            if (recorded is AgentStorageResult<AgentAuditEvent>.Failure failure)
            {
                logger.LogWarning("Не удалось сохранить отказ подтверждения: {Code}.", failure.Error.Code);
            }

            return new ReminderInteractionResult.Failure(new AppError(ErrorCode.Unauthorized, "Действие недоступно. Отправьте новый запрос."));
        }

        var started = clock.GetTimestamp();
        var changed = button.Confirm
            ? await actions.ConfirmActionAsync(owner, button.ActionId, context.UpdateId, cancellationToken)
            : await actions.CancelActionAsync(owner, button.ActionId, cancellationToken);
        if (changed is not AgentStorageResult<PreparedAction>.Success success)
        {
            var error = changed is AgentStorageResult<PreparedAction>.Failure failure ? failure.Error
                : new AppError(ErrorCode.Unauthorized, "Действие недоступно.");
            await AuditAsync(original.Value, context.UpdateId, AuditStatus.Rejected, "Действие отклонено: " + error.Code, started, cancellationToken);
            return Failure(error);
        }

        var action = success.Value;
        if (!button.Confirm)
        {
            await AuditAsync(action, context.UpdateId, AuditStatus.Cancelled, "Пользователь отменил подготовленное напоминание.", started, cancellationToken);
            return new ReminderInteractionResult.Success(RequestChecks.LlmText("Создание напоминания отменено."), true);
        }

        var result = await runtime.ExecuteReminderAsync(action, cancellationToken);
        if (result is not AgentStorageResult<ReminderCreation>.Success saved)
        {
            var error = result is AgentStorageResult<ReminderCreation>.Failure failure ? failure.Error
                : new AppError(ErrorCode.InvalidAgentData, "Не удалось проверить сохранённое напоминание.");
            await AuditAsync(action, context.UpdateId, AuditStatus.Failed, "Создание не подтверждено результатом MCP: " + error.Code, started, cancellationToken);
            return Failure(error);
        }

        var reminder = saved.Value.Reminder;
        await AuditAsync(action, context.UpdateId, AuditStatus.Succeeded,
            saved.Value.Created ? "Подтверждённое напоминание создано." : "Возвращено ранее созданное напоминание без повторной записи.", started, cancellationToken);
        var prefix = saved.Value.Created ? "Напоминание создано." : "Это напоминание уже было создано. Повторная запись не создавалась.";
        var text = $"{prefix}\nID: {reminder.Id}\nВремя: {reminder.Payload.Schedule.Moment.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)} ({reminder.Payload.Schedule.TimeZone.Value.Id}).";
        return new ReminderInteractionResult.Success(RequestChecks.LlmText(text), true);
    }

    private ReminderInteractionResult Failure(AppError error)
    {
        return new ReminderInteractionResult.Failure(error,
            error.Code is ErrorCode.ActionExpired or ErrorCode.ActionCancelled or ErrorCode.ReminderTimeInPast);
    }

    private async Task AuditAsync(PreparedAction action, int update, AuditStatus status, string reason, long started, CancellationToken cancellationToken)
    {
        var entry = new AgentAuditEvent(AgentChecks.AgentRequestId(Guid.NewGuid()), action.Owner.OwnerId, action.Owner.ChatId,
            update, clock.GetUtcNow(), AuditAction.ToolCall, status, AgentChecks.AuditReason(reason),
            new AuditArgumentsSummary(action.Payload.Text.Length, action.Payload.Schedule.Moment, action.Payload.Schedule.TimeZone),
            AgentChecks.McpToolName("add_reminder"), clock.GetElapsedTime(started));
        var saved = await audit.AppendEventAsync(entry, cancellationToken);
        if (saved is AgentStorageResult<AgentAuditEvent>.Failure failure)
        {
            logger.LogWarning("Не удалось сохранить аудит подтверждения: {Code}.", failure.Error.Code);
        }
    }
}
