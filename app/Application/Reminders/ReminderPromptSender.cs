using System.Globalization;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Application.Reminders;

public sealed class ReminderPromptSender(ITelegramGateway telegram, ReminderCallbackCodec codec, TimeProvider clock)
{
    public Task<Result<int>> SendAsync(PreparedAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.Status != PreparedActionStatus.Pending || action.ExpiresAt <= clock.GetUtcNow())
        {
            return Task.FromResult<Result<int>>(new Result<int>.Failure(new AppError(ErrorCode.ActionExpired, "Подготовьте новое напоминание.")));
        }

        var timezone = action.Payload.Schedule.TimeZone;
        string text = TelegramChecks.TelegramText($"Создать напоминание?\nТекст: {action.Payload.Text}\nВремя: {action.Payload.Schedule.Moment.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)} ({timezone.Value.Id})\nПодтвердите до {timezone.ConvertInstant(action.ExpiresAt).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}. До подтверждения напоминание не создаётся.");
        var keyboard = new TelegramInlineKeyboard(new IReadOnlyList<TelegramInlineButton>[]
        {
            new TelegramInlineButton[]
            {
                new(TelegramChecks.TelegramText("✅ Подтвердить"), codec.Encode(new ReminderButtonAction(action.Id, true))),
                new(TelegramChecks.TelegramText("❌ Отменить"), codec.Encode(new ReminderButtonAction(action.Id, false))),
            },
        });
        return telegram.SendInlineTextAsync(action.Owner.ChatId, text, keyboard, cancellationToken);
    }
}
