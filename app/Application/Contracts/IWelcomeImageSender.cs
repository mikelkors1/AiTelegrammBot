using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IWelcomeImageSender
{
    Task<TelegramSendResult> SendAsync(ChatId chatId, LlmText caption, CancellationToken cancellationToken);
}
