namespace ItmoBot.Application.Conversations;

public sealed record class ConversationState(long ChatId, AssistantMode Mode, decimal Temperature, long Revision);
