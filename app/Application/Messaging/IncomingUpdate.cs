namespace ItmoBot.Application.Messaging;

public sealed record class IncomingUpdate(int Id, long? ChatId, string? Text, long? OwnerId = null, TelegramCallback? Callback = null);
