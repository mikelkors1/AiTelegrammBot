using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class BotMenuButton(TelegramText Label, TelegramText Command);
