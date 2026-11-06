using ItmoBot.Application.Commands;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Messaging;

public sealed class TelegramTextSender(ITelegramGateway telegram, TelegramReplyFormatter formatter, BotMenu menu, ILogger logger)
{
    public async Task<TextDeliveryResult> SendAsync(long chatId, string text, CancellationToken cancellationToken,
        BotMenuPage menuPage = BotMenuPage.Main)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = formatter.Format(text);
        var delivered = 0;
        foreach (var part in parts)
        {
            Result<int> sent = await telegram.SendTextAsync(chatId, part.Text, cancellationToken, part.Format,
                delivered == 0 ? menu.GetKeyboard(menuPage) : null);
            if (sent is Result<int>.Failure sendFailure)
            {
                logger.LogWarning("Доставка ответа прервана: {Code}, отправлено частей {Count}.", sendFailure.Error.Code, delivered);
                return new TextDeliveryResult.Failure(sendFailure.Error, delivered);
            }

            delivered++;
        }

        return new TextDeliveryResult.Success(delivered);
    }
}
