namespace ItmoBot.Application.Messaging;

public sealed record class TelegramInlineKeyboard(IReadOnlyList<IReadOnlyList<TelegramInlineButton>> Rows);
