namespace ItmoBot.Application.Messaging;

public sealed record class TelegramCallback(string Id, string Data, int? MessageId);
