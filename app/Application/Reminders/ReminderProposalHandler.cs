using System.Globalization;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;

namespace ItmoBot.Application.Reminders;

public sealed class ReminderProposalHandler(IPreparedActionRepository actions, string model) : IReminderProposalHandler
{
    public async Task<AgentCompletionResult> PrepareAsync(LlmToolCall call, AgentTurnContext? context, UserTimeZone? timezone, CancellationToken cancellationToken)
    {
        if (context is null || context.Owner != context.ChatId)
        {
            return new AgentCompletionResult.Failure(new AppError(ErrorCode.Unauthorized, "Для напоминания нужен личный чат с подтверждённым пользователем."));
        }

        if (timezone is null)
        {
            return new AgentCompletionResult.Success(new LlmResponse(RequestChecks.LlmText("Сначала выберите часовой пояс: /timezone Europe/Moscow (или другую зону IANA)."), model, null, null));
        }

        ReminderPayload payload;
        try
        {
            var time = call.Arguments.GetProperty("remind_at").GetString()!;
            if (time.EndsWith('Z'))
            {
                time = time[..^1] + "+00:00";
            }

            var moment = DateTimeOffset.ParseExact(time, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"], CultureInfo.InvariantCulture, DateTimeStyles.None);
            payload = new ReminderPayload(AgentChecks.ReminderText(call.Arguments.GetProperty("text").GetString()!), new ReminderSchedule(moment, timezone));
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return new AgentCompletionResult.Failure(new AppError(ErrorCode.InvalidArguments, "Уточните время напоминания и его часовой пояс."));
        }

        var stored = await actions.PrepareReminderAsync(new ActionOwner(context.Owner, context.ChatId), context.UpdateId, payload, cancellationToken);
        if (stored is AgentStorageResult<PreparedAction>.Failure failure)
        {
            return new AgentCompletionResult.Failure(failure.Error);
        }

        if (stored is not AgentStorageResult<PreparedAction>.Success success)
        {
            return new AgentCompletionResult.Failure(new AppError(ErrorCode.InvalidAgentData, "Не удалось подготовить действие."));
        }

        var action = success.Value;
        var text = action.Status switch
        {
            PreparedActionStatus.Pending => $"Подготовлено напоминание: {action.Payload.Text}\nВремя: {action.Payload.Schedule.Moment.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)} ({timezone.Value.Id}).\nДля создания требуется ваше подтверждение. Срок подтверждения — 5 минут.",
            PreparedActionStatus.Executed => "Это действие уже выполнено. Повторное напоминание не создавалось.",
            PreparedActionStatus.Confirmed => "Действие уже подтверждено и ожидает выполнения.",
            _ => "Предыдущее действие отменено или срок его подтверждения истёк. Отправьте новый запрос.",
        };
        return new AgentCompletionResult.Success(new LlmResponse(RequestChecks.LlmText(text), model, null, null),
            action.Status == PreparedActionStatus.Pending ? action : null);
    }
}
