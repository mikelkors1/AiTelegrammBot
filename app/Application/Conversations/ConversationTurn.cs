namespace ItmoBot.Application.Conversations;

public sealed record class ConversationTurn(Guid Id, ConversationState State);
