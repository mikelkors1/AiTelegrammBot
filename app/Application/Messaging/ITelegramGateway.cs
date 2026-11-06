using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Messaging;

public interface ITelegramGateway : IDisposable
{
    Task<Result<string>> InitializeAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<IncomingUpdate>>> GetUpdatesAsync(int offset, CancellationToken cancellationToken);
    Task<Result<int>> SendTextAsync(long chatId, string text, CancellationToken cancellationToken,
        TelegramTextFormat format = TelegramTextFormat.Plain, TelegramReplyKeyboard? keyboard = null);
    Task<Result> DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken);
    Task<Result<int>> SendPhotoAsync(long chatId, Stream photo, CancellationToken cancellationToken,
        string? caption = null, TelegramReplyKeyboard? keyboard = null);
    Task<Result<int>> SendInlineTextAsync(long chatId, string text, TelegramInlineKeyboard keyboard, CancellationToken cancellationToken);
    Task<Result> AnswerCallbackAsync(string callbackId, CancellationToken cancellationToken);
    Task<Result> RemoveInlineKeyboardAsync(long chatId, int messageId, CancellationToken cancellationToken);

}
