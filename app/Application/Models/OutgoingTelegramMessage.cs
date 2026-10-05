using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class OutgoingTelegramMessage(TelegramText Text, TelegramTextFormat Format);
