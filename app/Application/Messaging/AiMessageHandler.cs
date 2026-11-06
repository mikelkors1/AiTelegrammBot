using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Commands;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Reminders;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Application.Messaging;

public sealed class AiMessageHandler(
    ConversationLock conversations,
    BotCommandParser parser,
    BotCommandHandler commands,
    IDialogueService dialogue,
    TelegramTextSender sender,
    WelcomeImageSender welcomeImage,
    UserErrorPresenter errors,
    ILogger logger, AgentCommandHandler? agentCommands = null, ReminderInteractionHandler? interactions = null,
    ReminderPromptSender? reminderPrompts = null) : IMessageHandler
{
    public async Task<Result<bool>> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (update.ChatId is not { } chatId)
        {
            return new Result<bool>.Success(Value: false);
        }

        try
        {
            if (update.Callback is not null)
            {
                return interactions is not null ? await interactions.HandleAsync(update, cancellationToken)
                    : new Result<bool>.Failure(new AppError(ErrorCode.ApplicationNotReady, "Подтверждения недоступны."));
            }

            if (update.Text is not { } text)
            {
                return new Result<bool>.Success(false);
            }

            await using var acquired = await conversations.AcquireAsync(chatId, cancellationToken);
            return await DispatchAsync(chatId, text, update, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("Непредвиденная ошибка обработки сообщения: {Code}.", ErrorCode.UnexpectedFailure);
            return await ReportUnexpectedAsync(chatId, cancellationToken);
        }
    }

    private async Task<Result<bool>> DispatchAsync(long chatId, string text, IncomingUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            var error = new AppError(ErrorCode.InvalidLlmText, "Сообщение не содержит текста.");
            return await ReportAsync(chatId, error, errors.Format(error), cancellationToken);
        }

        var parsed = parser.Parse(text);
        if (parsed is BotCommandParseResult.Failure parseFailure)
        {
            return await ReportAsync(chatId, parseFailure.Error, errors.Format(parseFailure.Error), cancellationToken);
        }

        if (parsed is BotCommandParseResult.Success command)
        {
            return await ExecuteCommandAsync(chatId, command.Command, update, cancellationToken);
        }

        Result<DialogueReply> result = await dialogue.RespondAsync(chatId, RequestChecks.LlmText(text), cancellationToken,
            update.OwnerId is { } owner ? new AgentTurnContext(owner, chatId, update.Id) : null);
        if (result is Result<DialogueReply>.Failure dialogueFailure)
        {
            return await ReportAsync(chatId, dialogueFailure.Error, errors.Format(dialogueFailure.Error), cancellationToken);
        }

        var reply = ((Result<DialogueReply>.Success)result).Value;
        if (reply.PendingAction is { } pending && reminderPrompts is not null)
        {
            if (pending.Owner.ChatId != chatId || pending.Owner.OwnerId != update.OwnerId)
            {
                var error = new AppError(ErrorCode.Unauthorized, "Владелец подготовленного действия не совпадает с отправителем.");
                return await ReportAsync(chatId, error, errors.Format(error), cancellationToken);
            }

            Result<int> sent = await reminderPrompts.SendAsync(pending, cancellationToken);
            return sent is Result<int>.Failure failure
                ? await ReportAsync(chatId, failure.Error, errors.Format(failure.Error), cancellationToken)
                : new Result<bool>.Success(true);
        }

        return await DeliverAsync(chatId, reply.Response.Text, cancellationToken);
    }

    private async Task<Result<bool>> ExecuteCommandAsync(long chatId, BotCommand command, IncomingUpdate update, CancellationToken cancellationToken)
    {
        Result<string> result;
        if (command is BotCommand.ShowTools or BotCommand.ShowTimeZone or BotCommand.SetTimeZone or BotCommand.ShowWhy)
        {
            result = agentCommands is null
                ? new Result<string>.Failure(new AppError(ErrorCode.ApplicationNotReady, "Команды агента недоступны."))
                : await agentCommands.ExecuteAsync(update.OwnerId is { } owner ? new AgentTurnContext(owner, chatId, update.Id) : null, command, cancellationToken);
        }
        else
        {
            result = await commands.ExecuteAsync(chatId, command, cancellationToken);
        }
        if (result is Result<string>.Failure failure)
        {
            return await ReportAsync(chatId, failure.Error, errors.Format(failure.Error), cancellationToken);
        }

        if (command is BotCommand.Start)
        {
            Result<int> image = await welcomeImage.SendAsync(chatId, ((Result<string>.Success)result).Value, cancellationToken);
            if (image is Result<int>.Success)
            {
                return new Result<bool>.Success(Value: true);
            }

            if (image is Result<int>.Failure imageFailure)
            {
                logger.LogWarning("Не удалось отправить приветственное изображение: {Code}.", imageFailure.Error.Code);
            }
        }

        var menuPage = command is BotCommand.ShowSettings or BotCommand.ChangeCreativity
            ? BotMenuPage.Settings
            : BotMenuPage.Main;
        return await DeliverAsync(chatId, ((Result<string>.Success)result).Value, cancellationToken, menuPage);
    }

    private async Task<Result<bool>> DeliverAsync(long chatId, string text, CancellationToken cancellationToken,
        BotMenuPage menuPage = BotMenuPage.Main)
    {
        var delivered = await sender.SendAsync(chatId, text, cancellationToken, menuPage);
        if (delivered is TextDeliveryResult.Failure failure)
        {
            return await ReportAsync(chatId, failure.Error, errors.DeliveryFailure(), cancellationToken);
        }

        return new Result<bool>.Success(Value: true);
    }

    private async Task<Result<bool>> ReportAsync(long chatId, AppError error, string message, CancellationToken cancellationToken)
    {
        var sent = await sender.SendAsync(chatId, message, cancellationToken);
        if (sent is TextDeliveryResult.Failure failure)
        {
            var relevant = error.Code is ErrorCode.TelegramUnauthorized or ErrorCode.TelegramPollingConflict
                ? error
                : failure.Error;
            return new Result<bool>.Failure(SafeError(relevant));
        }

        return new Result<bool>.Failure(SafeError(error));
    }

    private AppError SafeError(AppError error)
    {
        return new AppError(error.Code, errors.Format(error), error.IsRetryable);
    }

    private async Task<Result<bool>> ReportUnexpectedAsync(long chatId, CancellationToken cancellationToken)
    {
        var error = new AppError(ErrorCode.UnexpectedFailure, "Не удалось обработать запрос.");
        try
        {
            return await ReportAsync(chatId, error, errors.Format(error), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new Result<bool>.Failure(error);
        }
    }
}
