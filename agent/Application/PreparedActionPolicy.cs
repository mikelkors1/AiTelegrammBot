using ItmoBot.Application.Agent.Models;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Agent;

public sealed class PreparedActionPolicy
{
    public AppError? ValidatePreparation(UserTimeZone? timezone, ReminderPayload payload, DateTimeOffset now)
    {
        if (timezone is null)
        {
            return new AppError(ErrorCode.TimeZoneNotSet, "Сначала укажите часовой пояс.");
        }

        if (timezone.Value.Id != payload.Schedule.TimeZone.Value.Id)
        {
            return new AppError(ErrorCode.ActionMismatch, "Часовой пояс изменился. Подготовьте действие заново.");
        }

        if (payload.Schedule.Moment <= now)
        {
            return new AppError(ErrorCode.ReminderTimeInPast, "Время напоминания должно быть в будущем.");
        }

        return null;
    }

    public AppError? ValidatePendingState(PreparedAction action, DateTimeOffset now)
    {
        return action.Status switch
        {
            PreparedActionStatus.Cancelled => new AppError(ErrorCode.ActionCancelled, "Действие отменено."),
            PreparedActionStatus.Expired => new AppError(ErrorCode.ActionExpired, "Срок подтверждения истёк."),
            PreparedActionStatus.Pending when action.ExpiresAt <= now => new AppError(ErrorCode.ActionExpired, "Срок подтверждения истёк."),
            _ => null,
        };
    }
}
