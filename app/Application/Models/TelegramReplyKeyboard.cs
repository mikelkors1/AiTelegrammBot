using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class TelegramReplyKeyboard(IReadOnlyList<IReadOnlyList<TelegramText>> Rows);
