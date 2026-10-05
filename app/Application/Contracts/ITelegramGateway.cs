using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Models;

namespace ItmoBot.Application.Contracts;

public interface ITelegramGateway : IDisposable
{
    Task<TelegramInitializationResult> InitializeAsync(CancellationToken cancellationToken);
    Task<TelegramPollingResult> GetUpdatesAsync(UpdateOffset offset, CancellationToken cancellationToken);
    Task<TelegramSendResult> SendTextAsync(ChatId chatId, TelegramText text, CancellationToken cancellationToken,
        TelegramTextFormat format = TelegramTextFormat.Plain, TelegramReplyKeyboard? keyboard = null);
    Task<TelegramDeleteResult> DeleteMessageAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken);
    Task<TelegramSendResult> SendPhotoAsync(ChatId chatId, Stream photo, CancellationToken cancellationToken,
        TelegramCaption? caption = null, TelegramReplyKeyboard? keyboard = null);
}
