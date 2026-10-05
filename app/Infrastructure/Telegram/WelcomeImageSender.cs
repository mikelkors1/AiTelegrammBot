using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Application.Models;

namespace ItmoBot.Infrastructure.Telegram;

public sealed class WelcomeImageSender(ITelegramGateway telegram, IBotMenu menu) : IWelcomeImageSender
{
    public async Task<TelegramSendResult> SendAsync(ChatId chatId, LlmText caption, CancellationToken cancellationToken)
    {
        using var photo = typeof(WelcomeImageSender).Assembly.GetManifestResourceStream("ItmoBot.Telegram.Welcome.png");
        if (photo is null)
        {
            return new TelegramSendResult.Failure(new AppError(ErrorCode.UnexpectedFailure, "Изображение приветствия недоступно."));
        }

        try
        {
            return await telegram.SendPhotoAsync(chatId, photo, cancellationToken,
                new TelegramCaption(caption.Value), menu.GetKeyboard(BotMenuPage.Main));
        }
        catch (ValueObjectValidationException error)
        {
            return new TelegramSendResult.Failure(new AppError(error.Code, error.Message));
        }
    }
}
