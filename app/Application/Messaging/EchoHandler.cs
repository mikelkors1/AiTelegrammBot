using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Messaging;

public sealed class EchoHandler(ITelegramGateway telegram) : IMessageHandler
{
    public async Task<Result<bool>> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        if (update is not { ChatId: { } chatId, Text: { } text })
        {
            return new Result<bool>.Success(Value: false);
        }

        Result<int> sent = await telegram.SendTextAsync(chatId, text, cancellationToken);
        if (sent is Result<int>.Failure sendFailure)
        {
            return new Result<bool>.Failure(sendFailure.Error);
        }

        return new Result<bool>.Success(Value: true);
    }
}
