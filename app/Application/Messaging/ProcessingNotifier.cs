using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Application.Messaging;

public sealed class ProcessingNotifier(ITelegramGateway telegram)
{
    public Task<Result<int>> NotifyAsync(long chatId, CancellationToken cancellationToken)
    {
        return telegram.SendTextAsync(chatId, TelegramChecks.TelegramText("Готовлю ответ…"), cancellationToken);
    }

    public Task<Result> DismissAsync(long chatId, int messageId, CancellationToken cancellationToken)
    {
        return telegram.DeleteMessageAsync(chatId, messageId, cancellationToken);
    }
}
