using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Handlers;

public sealed class AiMessageHandler(
    IConversationLock conversations,
    IBotCommandParser parser,
    IBotCommandHandler commands,
    IDialogueService dialogue,
    ITextSender sender,
    IWelcomeImageSender welcomeImage,
    IUserErrorPresenter errors,
    ILogger logger) : IMessageHandler
{
    public async Task<MessageHandlingResult> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (update is not { ChatId: { } chatId, Text: { } text })
        {
            return new MessageHandlingResult.Success(WasHandled: false);
        }

        await using var acquired = await conversations.AcquireAsync(chatId, cancellationToken);
        try
        {
            return await DispatchAsync(chatId, text, cancellationToken);
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

    private async Task<MessageHandlingResult> DispatchAsync(ChatId chatId, TelegramText text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text.Value))
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
            return await ExecuteCommandAsync(chatId, command.Command, cancellationToken);
        }

        var result = await dialogue.RespondAsync(chatId, new LlmText(text.Value), cancellationToken);
        if (result is DialogueResult.Failure dialogueFailure)
        {
            return await ReportAsync(chatId, dialogueFailure.Error, errors.Format(dialogueFailure.Error), cancellationToken);
        }

        return await DeliverAsync(chatId, ((DialogueResult.Success)result).Reply.Response.Text, cancellationToken);
    }

    private async Task<MessageHandlingResult> ExecuteCommandAsync(ChatId chatId, BotCommand command, CancellationToken cancellationToken)
    {
        var result = await commands.ExecuteAsync(chatId, command, cancellationToken);
        if (result is BotCommandExecutionResult.Failure failure)
        {
            return await ReportAsync(chatId, failure.Error, errors.Format(failure.Error), cancellationToken);
        }

        if (command is BotCommand.Start)
        {
            var image = await welcomeImage.SendAsync(chatId, ((BotCommandExecutionResult.Success)result).Reply, cancellationToken);
            if (image is TelegramSendResult.Success)
            {
                return new MessageHandlingResult.Success(WasHandled: true);
            }

            if (image is TelegramSendResult.Failure imageFailure)
            {
                logger.LogWarning("Не удалось отправить приветственное изображение: {Code}.", imageFailure.Error.Code);
            }
        }

        var menuPage = command is BotCommand.ShowSettings or BotCommand.ChangeCreativity
            ? BotMenuPage.Settings
            : BotMenuPage.Main;
        return await DeliverAsync(chatId, ((BotCommandExecutionResult.Success)result).Reply, cancellationToken, menuPage);
    }

    private async Task<MessageHandlingResult> DeliverAsync(ChatId chatId, LlmText text, CancellationToken cancellationToken,
        BotMenuPage menuPage = BotMenuPage.Main)
    {
        var delivered = await sender.SendAsync(chatId, text, cancellationToken, menuPage);
        if (delivered is TextDeliveryResult.Failure failure)
        {
            return await ReportAsync(chatId, failure.Error, errors.DeliveryFailure(), cancellationToken);
        }

        return new MessageHandlingResult.Success(WasHandled: true);
    }

    private async Task<MessageHandlingResult> ReportAsync(ChatId chatId, AppError error, LlmText message, CancellationToken cancellationToken)
    {
        var sent = await sender.SendAsync(chatId, message, cancellationToken);
        if (sent is TextDeliveryResult.Failure failure)
        {
            var relevant = error.Code is ErrorCode.TelegramUnauthorized or ErrorCode.TelegramPollingConflict
                ? error
                : failure.Error;
            return new MessageHandlingResult.Failure(SafeError(relevant));
        }

        return new MessageHandlingResult.Failure(SafeError(error));
    }

    private AppError SafeError(AppError error)
    {
        return new AppError(error.Code, errors.Format(error).Value, error.IsRetryable);
    }

    private async Task<MessageHandlingResult> ReportUnexpectedAsync(ChatId chatId, CancellationToken cancellationToken)
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
            return new MessageHandlingResult.Failure(error);
        }
    }
}
