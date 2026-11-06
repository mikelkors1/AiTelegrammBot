namespace ItmoBot.Application.Messaging;

public sealed record class TelegramReplyKeyboard(IReadOnlyList<IReadOnlyList<string>> Rows);
