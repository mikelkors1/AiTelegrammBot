using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Models;

namespace ItmoBot.Application.Contracts;

public interface ITextSender
{
    Task<TextDeliveryResult> SendAsync(ChatId chatId, LlmText text, CancellationToken cancellationToken,
        BotMenuPage menuPage = BotMenuPage.Main);
}
