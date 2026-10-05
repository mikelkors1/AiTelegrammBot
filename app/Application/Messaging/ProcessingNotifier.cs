using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Messaging;

public sealed class ProcessingNotifier(ITelegramGateway telegram) : IProcessingNotifier
{
    public Task<TelegramSendResult> NotifyAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        return telegram.SendTextAsync(chatId, new TelegramText("Готовлю ответ…"), cancellationToken);
    }

    public Task<TelegramDeleteResult> DismissAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken)
    {
        return telegram.DeleteMessageAsync(chatId, messageId, cancellationToken);
    }
}
