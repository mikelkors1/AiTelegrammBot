using ItmoBot.Application.Commands;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Infrastructure.Telegram;

public sealed class WelcomeImageSender(ITelegramGateway telegram, BotMenu menu)
{
    public async Task<Result<int>> SendAsync(long chatId, string caption, CancellationToken cancellationToken)
    {
        using var photo = typeof(WelcomeImageSender).Assembly.GetManifestResourceStream("ItmoBot.Telegram.Welcome.png");
        if (photo is null)
        {
            return new Result<int>.Failure(new AppError(ErrorCode.UnexpectedFailure, "Изображение приветствия недоступно."));
        }

        try
        {
            return await telegram.SendPhotoAsync(chatId, photo, cancellationToken,
                TelegramChecks.TelegramCaption(caption), menu.GetKeyboard(BotMenuPage.Main));
        }
        catch (InputValidationException error)
        {
            return new Result<int>.Failure(new AppError(error.Code, error.Message));
        }
    }
}
