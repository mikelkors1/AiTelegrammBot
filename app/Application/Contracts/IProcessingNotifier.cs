using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IProcessingNotifier
{
    Task<TelegramSendResult> NotifyAsync(ChatId chatId, CancellationToken cancellationToken);
    Task<TelegramDeleteResult> DismissAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken);
}
