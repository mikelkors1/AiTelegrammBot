namespace ItmoBot.Application.Messaging;

public sealed record class OutgoingTelegramMessage(string Text, TelegramTextFormat Format);
