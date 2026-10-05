using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Handlers;

public sealed class EchoHandler(ITelegramGateway telegram) : IMessageHandler
{
    public async Task<MessageHandlingResult> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        if (update is not { ChatId: { } chatId, Text: { } text })
        {
            return new MessageHandlingResult.Success(WasHandled: false);
        }

        var sent = await telegram.SendTextAsync(chatId, text, cancellationToken);
        if (sent is TelegramSendResult.Failure sendFailure)
        {
            return new MessageHandlingResult.Failure(sendFailure.Error);
        }

        return new MessageHandlingResult.Success(WasHandled: true);
    }
}
