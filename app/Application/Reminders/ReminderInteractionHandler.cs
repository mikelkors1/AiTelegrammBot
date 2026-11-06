using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Reminders;

public sealed class ReminderInteractionHandler(ITelegramGateway telegram, ReminderCallbackCodec codec,
    ReminderConfirmationService confirmations, TelegramTextSender sender, UserErrorPresenter errors, ILogger logger)
{
    public async Task<Result<bool>> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        if (update.Callback is not { } callback || update.ChatId is not { } chat)
        {
            return new Result<bool>.Success(false);
        }

        using (var acknowledgment = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            acknowledgment.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var acknowledged = await telegram.AnswerCallbackAsync(callback.Id, acknowledgment.Token);
                if (acknowledged is Result.Failure failure)
                {
                    logger.LogWarning("Не удалось подтвердить получение кнопки: {Code}.", failure.Error.Code);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Истёк срок ответа на callback-запрос.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ReminderInteractionResult result;
        var button = codec.Decode(callback.Data);
        if (button is null)
        {
            result = new ReminderInteractionResult.Failure(new AppError(ErrorCode.InvalidCallback, "Неизвестная кнопка. Отправьте новый запрос."));
        }
        else if (update.OwnerId is not { } owner)
        {
            result = new ReminderInteractionResult.Failure(new AppError(ErrorCode.Unauthorized, "Не удалось проверить владельца действия."));
        }
        else
        {
            result = await confirmations.ExecuteAsync(new AgentTurnContext(owner, chat, update.Id), button, cancellationToken);
        }

        var remove = result is ReminderInteractionResult.Success { RemoveButtons: true } or ReminderInteractionResult.Failure { RemoveButtons: true };
        if (remove && callback.MessageId is { } message)
        {
            await RemoveButtonsAsync(chat, message, cancellationToken);
        }

        string text = result is ReminderInteractionResult.Success success ? success.Reply : errors.Format(((ReminderInteractionResult.Failure)result).Error);
        var sent = await sender.SendAsync(chat, text, cancellationToken);
        if (sent is TextDeliveryResult.Failure delivery)
        {
            return new Result<bool>.Failure(delivery.Error);
        }

        return result is ReminderInteractionResult.Failure error
            ? new Result<bool>.Failure(error.Error)
            : new Result<bool>.Success(true);
    }

    private async Task RemoveButtonsAsync(long chat, int message, CancellationToken cancellationToken)
    {
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanup.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var result = await telegram.RemoveInlineKeyboardAsync(chat, message, cleanup.Token).WaitAsync(cleanup.Token);
            if (result is Result.Failure failure)
            {
                logger.LogWarning("Не удалось убрать кнопки завершённого действия: {Code}.", failure.Error.Code);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("Не удалось убрать кнопки завершённого действия.");
        }
    }

}
