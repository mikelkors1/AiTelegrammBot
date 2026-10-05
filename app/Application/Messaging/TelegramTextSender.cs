using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Models;

namespace ItmoBot.Application.Messaging;

public sealed class TelegramTextSender(ITelegramGateway telegram, ITelegramReplyFormatter formatter, IBotMenu menu, ILogger logger) : ITextSender
{
    public async Task<TextDeliveryResult> SendAsync(ChatId chatId, LlmText text, CancellationToken cancellationToken,
        BotMenuPage menuPage = BotMenuPage.Main)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = formatter.Format(text);
        var delivered = 0;
        foreach (var part in parts)
        {
            var sent = await telegram.SendTextAsync(chatId, part.Text, cancellationToken, part.Format,
                delivered == 0 ? menu.GetKeyboard(menuPage) : null);
            if (sent is TelegramSendResult.Failure sendFailure)
            {
                logger.LogWarning("Доставка ответа прервана: {Code}, отправлено частей {Count}.", sendFailure.Error.Code, delivered);
                return new TextDeliveryResult.Failure(sendFailure.Error, new MessagePartCount(delivered));
            }

            delivered++;
        }

        return new TextDeliveryResult.Success(new MessagePartCount(delivered));
    }
}
