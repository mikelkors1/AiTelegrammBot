namespace ItmoBot.Application.Messaging;

public sealed record class ReplySegment(string Text, bool IsCode, string? Language);
